using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Creates provider clients from the current configuration.
/// </summary>
public interface IOidcProviderFactory
{
    OidcProvider Get(OidcProviderConfig providerConfig);
}
