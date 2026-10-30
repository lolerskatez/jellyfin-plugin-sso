using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Builds public URLs and validates post-login return paths.
/// </summary>
public static class SsoUrlBuilder
{
    /// <summary>
    /// Gets the externally visible Jellyfin base URL.
    /// </summary>
    public static string GetPublicBaseUrl(HttpRequest request, PluginConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.PublicBaseUrl))
        {
            return configuration.PublicBaseUrl.TrimEnd('/');
        }

        return (request.Scheme + "://" + request.Host + request.PathBase).TrimEnd('/');
    }

    /// <summary>
    /// Gets the OIDC callback URL for a provider.
    /// </summary>
    public static string GetCallbackUrl(HttpRequest request, PluginConfiguration configuration, OidcProviderConfig provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.CallbackUrlOverride))
        {
            return provider.CallbackUrlOverride;
        }

        var baseUrl = !string.IsNullOrWhiteSpace(provider.PublicBaseUrlOverride)
            ? provider.PublicBaseUrlOverride.TrimEnd('/')
            : GetPublicBaseUrl(request, configuration);

        return baseUrl + "/sso/callback/" + Uri.EscapeDataString(provider.Slug);
    }

    /// <summary>
    /// Gets the post-logout redirect URL for a provider.
    /// </summary>
    public static string GetPostLogoutRedirectUri(HttpRequest request, PluginConfiguration configuration, OidcProviderConfig provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.PostLogoutRedirectUri))
        {
            return provider.PostLogoutRedirectUri.Trim();
        }

        var baseUrl = !string.IsNullOrWhiteSpace(provider.PublicBaseUrlOverride)
            ? provider.PublicBaseUrlOverride.TrimEnd('/')
            : GetPublicBaseUrl(request, configuration);

        return baseUrl + "/web/index.html";
    }

    /// <summary>
    /// Normalizes a return URL to a same-origin Jellyfin web path.
    /// </summary>
    public static string NormalizeReturnUrl(string? value)
    {
        const string Default = "/web/index.html#/home";

        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        var decoded = value.Trim();
        if (decoded.Length > 500)
        {
            return Default;
        }

        try
        {
            decoded = Uri.UnescapeDataString(decoded);
        }
        catch (UriFormatException)
        {
            return Default;
        }

        if (!decoded.StartsWith('/')
            || decoded.StartsWith("//", StringComparison.Ordinal)
            || decoded.Contains('\\', StringComparison.Ordinal)
            || decoded.Any(char.IsControl))
        {
            return Default;
        }

        if (decoded.StartsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || decoded.StartsWith("/sso/", StringComparison.OrdinalIgnoreCase))
        {
            return decoded;
        }

        if (decoded.StartsWith("/web", StringComparison.OrdinalIgnoreCase))
        {
            return decoded;
        }

        return "/web/index.html#" + decoded;
    }

    /// <summary>
    /// Gets the URL for a successful login, relative to the server.
    /// </summary>
    public static string GetDefaultReturnUrl() => "/web/index.html#/home";
}
