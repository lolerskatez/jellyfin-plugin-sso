namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Configuration for one OpenID Connect provider.
/// </summary>
public class OidcProviderConfig
{
    /// <summary>
    /// Gets or sets the stable provider slug used in URLs.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider protocol. v1 supports oidc; the field exists so
    /// future SAML providers can be added without changing the configuration schema.
    /// </summary>
    public string Protocol { get; set; } = "oidc";

    /// <summary>
    /// Gets or sets the provider profile used for documentation and defaults.
    /// </summary>
    public string Profile { get; set; } = "generic";

    /// <summary>
    /// Gets or sets additional authorization request parameters, such as prompt or acr_values.
    /// </summary>
    public List<OidcAuthorizationParameter> AdditionalAuthorizationParameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the display name shown in the admin UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the button text shown on the login page.
    /// </summary>
    public string ButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this provider is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the display order of the provider button.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets an optional icon URL. Prefer a same-origin URL.
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OIDC authority/issuer URL.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional explicit discovery document URL.
    /// </summary>
    public string DiscoveryUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional issuer override when discovery uses a different internal URL.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OIDC client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OIDC client secret. Empty means a public client using PKCE.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested scopes as a space-separated list.
    /// </summary>
    public string Scopes { get; set; } = "openid profile email groups";

    /// <summary>
    /// Gets or sets a value indicating whether PKCE is used.
    /// </summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the UserInfo endpoint is queried.
    /// </summary>
    public bool FetchUserInfo { get; set; } = true;

    /// <summary>
    /// Gets or sets the OIDC response mode: query or form_post.
    /// </summary>
    public string ResponseMode { get; set; } = "query";

    /// <summary>
    /// Gets or sets the token endpoint authentication method: client_secret_post, client_secret_basic, or none.
    /// </summary>
    public string TokenEndpointAuthMethod { get; set; } = "client_secret_post";

    /// <summary>
    /// Gets or sets an optional full callback URL override.
    /// </summary>
    public string CallbackUrlOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional post-logout redirect URI. Empty uses the Jellyfin web index.
    /// </summary>
    public string PostLogoutRedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the claim used for the suggested Jellyfin username.
    /// </summary>
    public string UsernameClaim { get; set; } = "preferred_username";

    /// <summary>
    /// Gets or sets the claim used for the display name.
    /// </summary>
    public string DisplayNameClaim { get; set; } = "name";

    /// <summary>
    /// Gets or sets the claim used for the email address.
    /// </summary>
    public string EmailClaim { get; set; } = "email";

    /// <summary>
    /// Gets or sets the claim path(s) used for group/role values.
    /// </summary>
    public List<string> GroupClaimPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets the roles required to sign in through this provider.
    /// </summary>
    public List<string> RequiredRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets the roles that grant Jellyfin administrator permission.
    /// </summary>
    public List<string> AdminRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a verified email is required.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; }

    /// <summary>
    /// Gets or sets the allowed email domains. Empty means all domains.
    /// </summary>
    public List<string> AllowedEmailDomains { get; set; } = [];

    /// <summary>
    /// Gets or sets the username collision policy: Pending, Reject, or Suffix.
    /// </summary>
    public string UsernameConflictPolicy { get; set; } = "Pending";

    /// <summary>
    /// Gets or sets an optional per-provider auto-provision override.
    /// </summary>
    public bool? AutoProvision { get; set; }

    /// <summary>
    /// Gets or sets an optional per-provider approval override.
    /// </summary>
    public bool? RequireApproval { get; set; }

    /// <summary>
    /// Gets or sets an optional per-provider role-sync override.
    /// </summary>
    public bool? SyncRolesOnLogin { get; set; }

    /// <summary>
    /// Gets or sets an optional per-provider public base URL override.
    /// </summary>
    public string PublicBaseUrlOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether HTTP endpoints are allowed for this provider.
    /// </summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether HMAC-signed ID tokens are accepted.
    /// </summary>
    public bool AllowSymmetricSigning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider is treated as trusted for admin provisioning.
    /// </summary>
    public bool AllowAdminProvisioning { get; set; }
}

/// <summary>
/// One extra authorization request parameter.
/// </summary>
public class OidcAuthorizationParameter
{
    /// <summary>
    /// Gets or sets the parameter name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parameter value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
