using Jellyfin.Plugin.SSO.Accounts;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Persists pending login requests.
/// </summary>
public interface IPendingStore
{
    Task<PendingLoginRecord> AddOrGetAsync(PendingLoginRecord request, CancellationToken cancellationToken = default);

    Task<PendingLoginRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingLoginRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> DenyAsync(Guid id, CancellationToken cancellationToken = default);
}
