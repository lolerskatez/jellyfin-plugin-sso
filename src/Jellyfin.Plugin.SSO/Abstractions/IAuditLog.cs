using Jellyfin.Plugin.SSO.Accounts;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Writes and reads SSO audit events.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEvent>> ReadRecentAsync(int count, CancellationToken cancellationToken = default);
}
