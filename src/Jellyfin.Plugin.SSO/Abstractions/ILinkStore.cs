using Jellyfin.Plugin.SSO.Accounts;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Persists external identity links.
/// </summary>
public interface ILinkStore
{
    Task<IReadOnlyList<ExternalLinkRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ExternalLinkRecord?> FindAsync(string providerId, string issuer, string subject, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalLinkRecord>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(ExternalLinkRecord link, CancellationToken cancellationToken = default);

    Task TouchAsync(ExternalLinkRecord link, DateTimeOffset loginAt, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string providerId, Guid userId, CancellationToken cancellationToken = default);

    Task<int> RemoveUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
