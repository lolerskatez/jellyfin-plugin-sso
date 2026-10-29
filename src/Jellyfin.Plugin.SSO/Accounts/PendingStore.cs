using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// A login that could not be resolved automatically and requires administrator action.
/// </summary>
public sealed class PendingLoginRecord
{
    /// <summary>
    /// Gets or sets the request ID.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OIDC issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OIDC subject.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the suggested username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the external groups.
    /// </summary>
    public List<string> Groups { get; set; } = [];

    /// <summary>
    /// Gets or sets why administrator action is needed.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the request was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the request expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);

    /// <summary>
    /// Gets or sets the pending state.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Gets or sets the IP address that created the request. Stored as a hash.
    /// </summary>
    public string? IpHash { get; set; }
}

/// <summary>
/// File-backed store for pending login requests.
/// </summary>
public sealed class PendingStore : IPendingStore
{
    private readonly string _path;
    private readonly ILogger<PendingStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PendingStoreFile? _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="PendingStore"/> class.
    /// </summary>
    public PendingStore(IApplicationPaths applicationPaths, ILogger<PendingStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(applicationPaths.PluginConfigurationsPath, "sso", "pending.json");
    }

    /// <summary>
    /// Adds a pending request, or returns an existing pending request for the same identity.
    /// </summary>
    public async Task<PendingLoginRecord> AddOrGetAsync(
        PendingLoginRecord request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            Cleanup(data);

            var existing = data.Requests.FirstOrDefault(x =>
                string.Equals(x.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Issuer, request.Issuer, StringComparison.Ordinal)
                && string.Equals(x.Subject, request.Subject, StringComparison.Ordinal)
                && string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Username = request.Username ?? existing.Username;
                existing.DisplayName = request.DisplayName ?? existing.DisplayName;
                existing.Email = request.Email ?? existing.Email;
                existing.Groups = request.Groups;
                existing.Reason = request.Reason;
                await SaveAsync(data, cancellationToken).ConfigureAwait(false);
                return existing;
            }

            data.Requests.Add(request);
            await SaveAsync(data, cancellationToken).ConfigureAwait(false);
            return request;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gets a pending request by ID.
    /// </summary>
    public async Task<PendingLoginRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            Cleanup(data);
            return data.Requests.FirstOrDefault(x => x.Id == id && x.Status == "pending");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gets all pending requests.
    /// </summary>
    public async Task<IReadOnlyList<PendingLoginRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            Cleanup(data);
            return data.Requests
                .Where(x => x.Status == "pending")
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Marks a request as approved and removes it from the active queue.
    /// </summary>
    public async Task<bool> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ChangeStatusAsync(id, "approved", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a request as denied and removes it from the active queue.
    /// </summary>
    public async Task<bool> DenyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ChangeStatusAsync(id, "denied", cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ChangeStatusAsync(Guid id, string status, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var request = data.Requests.FirstOrDefault(x => x.Id == id && x.Status == "pending");
            if (request is null)
            {
                return false;
            }

            request.Status = status;
            await SaveAsync(data, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Cleanup(PendingStoreFile data)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var request in data.Requests.Where(x => x.Status == "pending" && x.ExpiresAt <= now))
        {
            request.Status = "expired";
        }
    }

    private async Task<PendingStoreFile> LoadAsync(CancellationToken cancellationToken)
    {
        if (_data is not null)
        {
            return _data;
        }

        _data = await AtomicJsonFile.ReadAsync<PendingStoreFile>(_path, SsoJson.Options, _logger, cancellationToken)
            .ConfigureAwait(false) ?? new PendingStoreFile();

        _data.Requests ??= [];
        return _data;
    }

    private async Task SaveAsync(PendingStoreFile data, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(_path, data, SsoJson.Options, cancellationToken).ConfigureAwait(false);
        _data = data;
    }

    private sealed class PendingStoreFile
    {
        public int Version { get; set; } = 1;

        public List<PendingLoginRecord> Requests { get; set; } = [];
    }
}
