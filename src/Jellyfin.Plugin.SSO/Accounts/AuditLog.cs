using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SSO.Abstractions;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// One audit event.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>
    /// Gets or sets the event timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the event type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin user ID.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets a short, non-reversible subject fingerprint.
    /// </summary>
    public string? SubjectHash { get; set; }

    /// <summary>
    /// Gets or sets a short, non-reversible IP fingerprint.
    /// </summary>
    public string? IpHash { get; set; }

    /// <summary>
    /// Gets or sets the outcome.
    /// </summary>
    public string? Outcome { get; set; }

    /// <summary>
    /// Gets or sets a human-readable detail without secrets.
    /// </summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Append-only JSONL audit log with simple size-based rotation.
/// </summary>
public sealed class AuditLog : IAuditLog
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLog"/> class.
    /// </summary>
    public AuditLog(IApplicationPaths applicationPaths)
    {
        _path = Path.Combine(applicationPaths.PluginConfigurationsPath, "sso", "audit.jsonl");
    }

    /// <summary>
    /// Writes one audit event.
    /// </summary>
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            RotateIfNeeded();
            var line = JsonSerializer.Serialize(auditEvent, JsonOptions);
            await File.AppendAllTextAsync(_path, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the most recent events.
    /// </summary>
    public async Task<IReadOnlyList<AuditEvent>> ReadRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
            var result = new List<AuditEvent>(Math.Min(count, lines.Length));

            foreach (var line in lines.Reverse())
            {
                if (result.Count >= count)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var item = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions);
                    if (item is not null)
                    {
                        result.Add(item);
                    }
                }
                catch (JsonException)
                {
                    // Ignore a truncated or manually edited line.
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Produces a short, non-reversible fingerprint safe for logs.
    /// </summary>
    public static string Fingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < MaxFileSize)
        {
            return;
        }

        var backup = _path + ".1";
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        File.Move(_path, backup);
    }
}
