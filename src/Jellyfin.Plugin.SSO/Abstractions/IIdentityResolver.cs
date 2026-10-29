using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Resolves external identities to Jellyfin users and manages links.
/// </summary>
public interface IIdentityResolver
{
    Task<IdentityResolution> ResolveLoginAsync(OidcIdentity identity, OidcProviderConfig provider, string remoteIp, CancellationToken cancellationToken = default);

    Task<IdentityResolution> LinkAsync(OidcIdentity identity, OidcProviderConfig provider, Guid jellyfinUserId, CancellationToken cancellationToken = default);

    Task<bool> UnlinkAsync(Guid jellyfinUserId, string providerId, CancellationToken cancellationToken = default);

    Task<IdentityResolution> ApprovePendingAsync(Guid pendingId, Guid? jellyfinUserId, bool createUser, CancellationToken cancellationToken = default);

    Task<bool> DenyPendingAsync(Guid pendingId, CancellationToken cancellationToken = default);
}
