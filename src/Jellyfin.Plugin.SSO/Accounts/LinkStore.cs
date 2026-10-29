using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// A persistent link between an external OIDC identity and a Jellyfin user.
/// </summary>
public sealed class ExternalLinkRecord
{
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
    /// Gets or sets the Jellyfin user ID.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the last known email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the last known username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the last known display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets when the link was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the identity last signed in.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets the link source: auto, self, admin.
    /// </summary>
    public string Source { get; set; } = "auto";
}

/// <summary>
/// File-backed store for external identity links.
/// </summary>
public sealed class LinkStore : ILinkStore
{
    private readonly string _path;
    private readonly ILogger<LinkStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LinkStoreFile? _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkStore"/> class.
    /// </summary>
    public LinkStore(IApplicationPaths applicationPaths, ILogger<LinkStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(applicationPaths.PluginConfigurationsPath, "sso", "links.json");
    }

    /// <summary>
    /// Gets all links.
    /// </summary>
    public async Task<IReadOnlyList<ExternalLinkRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return data.Links.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Finds a link by provider, issuer, and subject.
    /// </summary>
    public async Task<ExternalLinkRecord?> FindAsync(
        string providerId,
        string issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return data.Links.FirstOrDefault(x =>
                string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Issuer, issuer, StringComparison.Ordinal)
                && string.Equals(x.Subject, subject, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gets all links for a Jellyfin user.
    /// </summary>
    public async Task<IReadOnlyList<ExternalLinkRecord>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return data.Links
                .Where(x => x.JellyfinUserId == userId)
                .OrderBy(x => x.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adds a new link. Throws if the same external identity or the same provider/user pair already exists.
    /// </summary>
    public async Task AddAsync(ExternalLinkRecord link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);

            if (data.Links.Any(x =>
                    string.Equals(x.ProviderId, link.ProviderId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Issuer, link.Issuer, StringComparison.Ordinal)
                    && string.Equals(x.Subject, link.Subject, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("This external identity is already linked.");
            }

            if (data.Links.Any(x =>
                    x.JellyfinUserId == link.JellyfinUserId
                    && string.Equals(x.ProviderId, link.ProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("This user is already linked to this provider.");
            }

            data.Links.Add(link);
            await SaveAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Updates metadata for an existing link.
    /// </summary>
    public async Task TouchAsync(
        ExternalLinkRecord link,
        DateTimeOffset loginAt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var existing = data.Links.FirstOrDefault(x =>
                string.Equals(x.ProviderId, link.ProviderId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Issuer, link.Issuer, StringComparison.Ordinal)
                && string.Equals(x.Subject, link.Subject, StringComparison.Ordinal));

            if (existing is null)
            {
                return;
            }

            existing.LastLoginAt = loginAt;
            existing.Email = link.Email ?? existing.Email;
            existing.Username = link.Username ?? existing.Username;
            existing.DisplayName = link.DisplayName ?? existing.DisplayName;
            await SaveAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes one link.
    /// </summary>
    public async Task<bool> RemoveAsync(
        string providerId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var removed = data.Links.RemoveAll(x =>
                x.JellyfinUserId == userId
                && string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                await SaveAsync(data, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes all links belonging to a deleted Jellyfin user.
    /// </summary>
    public async Task<int> RemoveUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var removed = data.Links.RemoveAll(x => x.JellyfinUserId == userId);
            if (removed > 0)
            {
                await SaveAsync(data, cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LinkStoreFile> LoadAsync(CancellationToken cancellationToken)
    {
        if (_data is not null)
        {
            return _data;
        }

        _data = await AtomicJsonFile.ReadAsync<LinkStoreFile>(_path, SsoJson.Options, _logger, cancellationToken)
            .ConfigureAwait(false) ?? new LinkStoreFile();

        _data.Links ??= [];
        return _data;
    }

    private async Task SaveAsync(LinkStoreFile data, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(_path, data, SsoJson.Options, cancellationToken).ConfigureAwait(false);
        _data = data;
    }

    private sealed class LinkStoreFile
    {
        public int Version { get; set; } = 1;

        public List<ExternalLinkRecord> Links { get; set; } = [];
    }
}
