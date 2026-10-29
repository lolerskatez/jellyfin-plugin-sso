using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Creates Jellyfin users for external identities.
/// </summary>
public interface IUserProvisioner
{
    Task<User> CreateAsync(
        OidcIdentity identity,
        OidcProviderConfig provider,
        string username,
        RoleEvaluation roles,
        string source,
        CancellationToken cancellationToken = default);
}
