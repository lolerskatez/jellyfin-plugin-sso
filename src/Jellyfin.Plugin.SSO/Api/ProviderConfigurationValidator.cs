using System.Text.RegularExpressions;
using Jellyfin.Plugin.SSO.Abstractions;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Validates provider settings before they are written to disk.
/// </summary>
public sealed class ProviderConfigurationValidator : IProviderConfigurationValidator
{
    private static readonly Regex SlugRegex = new("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$", RegexOptions.Compiled);

    /// <inheritdoc />
    public bool Validate(AdminProviderDto request, out string? validationError)
    {
        validationError = null;

        if (string.IsNullOrWhiteSpace(request.Slug) || !SlugRegex.IsMatch(request.Slug))
        {
            validationError = "Slug must contain 1-32 lowercase letters, numbers, or hyphens.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Protocol)
            && !string.Equals(request.Protocol, "oidc", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "Only the OIDC protocol is supported in this build.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            validationError = "Display name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Authority) && string.IsNullOrWhiteSpace(request.DiscoveryUrl))
        {
            validationError = "Authority or discovery URL is required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Authority)
            && !Uri.TryCreate(request.Authority, UriKind.Absolute, out _))
        {
            validationError = "Authority must be an absolute URL.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.DiscoveryUrl)
            && !Uri.TryCreate(request.DiscoveryUrl, UriKind.Absolute, out _))
        {
            validationError = "Discovery URL must be an absolute URL.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Issuer)
            && !Uri.TryCreate(request.Issuer, UriKind.Absolute, out _))
        {
            validationError = "Issuer must be an absolute URL.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.CallbackUrlOverride)
            && !Uri.TryCreate(request.CallbackUrlOverride, UriKind.Absolute, out _))
        {
            validationError = "Callback URL override must be an absolute URL.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.PostLogoutRedirectUri)
            && !Uri.TryCreate(request.PostLogoutRedirectUri, UriKind.Absolute, out _))
        {
            validationError = "Post-logout redirect URI must be an absolute URL.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.PublicBaseUrlOverride)
            && !Uri.TryCreate(request.PublicBaseUrlOverride, UriKind.Absolute, out _))
        {
            validationError = "Public base URL override must be an absolute URL.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            validationError = "Client ID is required.";
            return false;
        }

        if (!(request.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("openid", StringComparer.OrdinalIgnoreCase))
        {
            validationError = "The openid scope is required.";
            return false;
        }

        if (!string.Equals(request.ResponseMode, "query", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.ResponseMode, "form_post", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "Response mode must be query or form_post.";
            return false;
        }

        if (!string.Equals(request.TokenEndpointAuthMethod, "client_secret_post", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.TokenEndpointAuthMethod, "client_secret_basic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.TokenEndpointAuthMethod, "none", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "Token endpoint auth method must be client_secret_post, client_secret_basic, or none.";
            return false;
        }

        return true;
    }
}
