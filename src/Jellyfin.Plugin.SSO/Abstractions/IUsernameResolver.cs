using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Resolves safe, unique Jellyfin usernames from external identities.
/// </summary>
public interface IUsernameResolver
{
    string Resolve(OidcIdentity identity, OidcProviderConfig provider);

    string MakeUnique(string username);

    User? FindExisting(string username);
}
