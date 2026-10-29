using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Maps external groups to Jellyfin user policy.
/// </summary>
public interface IPolicyMapper
{
    RoleEvaluation Evaluate(OidcIdentity identity, OidcProviderConfig provider);

    Task ApplyNewUserPolicyAsync(User user, OidcProviderConfig provider, RoleEvaluation evaluation, CancellationToken cancellationToken = default);

    Task ApplySyncPolicyAsync(User user, OidcProviderConfig provider, RoleEvaluation evaluation, CancellationToken cancellationToken = default);

    bool ShouldSyncRoles(OidcProviderConfig provider);
}
