using Jellyfin.Plugin.SSO.Configuration;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Admin-facing provider configuration with the secret masked.
/// </summary>
public sealed class AdminProviderDto
{
    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider protocol.
    /// </summary>
    public string Protocol { get; set; } = "oidc";

    /// <summary>
    /// Gets or sets the provider profile.
    /// </summary>
    public string Profile { get; set; } = "generic";

    /// <summary>
    /// Gets or sets extra authorization parameters.
    /// </summary>
    public List<OidcAuthorizationParameter> AdditionalAuthorizationParameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the button text.
    /// </summary>
    public string ButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the provider is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authority.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the discovery URL override.
    /// </summary>
    public string DiscoveryUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the issuer override.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a client secret is already stored.
    /// </summary>
    public bool HasClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a new client secret. Empty means keep the existing value.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the stored secret should be cleared.
    /// </summary>
    public bool ClearClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes.
    /// </summary>
    public string Scopes { get; set; } = "openid profile email groups";

    /// <summary>
    /// Gets or sets a value indicating whether PKCE is used.
    /// </summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether UserInfo is fetched.
    /// </summary>
    public bool FetchUserInfo { get; set; } = true;

    /// <summary>
    /// Gets or sets the response mode.
    /// </summary>
    public string ResponseMode { get; set; } = "query";

    /// <summary>
    /// Gets or sets the token endpoint auth method.
    /// </summary>
    public string TokenEndpointAuthMethod { get; set; } = "client_secret_post";

    /// <summary>
    /// Gets or sets the callback URL override.
    /// </summary>
    public string CallbackUrlOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username claim.
    /// </summary>
    public string UsernameClaim { get; set; } = "preferred_username";

    /// <summary>
    /// Gets or sets the display name claim.
    /// </summary>
    public string DisplayNameClaim { get; set; } = "name";

    /// <summary>
    /// Gets or sets the email claim.
    /// </summary>
    public string EmailClaim { get; set; } = "email";

    /// <summary>
    /// Gets or sets the group claim paths.
    /// </summary>
    public List<string> GroupClaimPaths { get; set; } = ["groups"];

    /// <summary>
    /// Gets or sets required roles.
    /// </summary>
    public List<string> RequiredRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets admin roles.
    /// </summary>
    public List<string> AdminRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether email verification is required.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; }

    /// <summary>
    /// Gets or sets allowed email domains.
    /// </summary>
    public List<string> AllowedEmailDomains { get; set; } = [];

    /// <summary>
    /// Gets or sets the username conflict policy.
    /// </summary>
    public string UsernameConflictPolicy { get; set; } = "Pending";

    /// <summary>
    /// Gets or sets the auto-provision override.
    /// </summary>
    public bool? AutoProvision { get; set; }

    /// <summary>
    /// Gets or sets the approval override.
    /// </summary>
    public bool? RequireApproval { get; set; }

    /// <summary>
    /// Gets or sets the role-sync override.
    /// </summary>
    public bool? SyncRolesOnLogin { get; set; }

    /// <summary>
    /// Gets or sets the public base URL override.
    /// </summary>
    public string PublicBaseUrlOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether HTTP is allowed.
    /// </summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether symmetric signing is allowed.
    /// </summary>
    public bool AllowSymmetricSigning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether admin provisioning is allowed.
    /// </summary>
    public bool AllowAdminProvisioning { get; set; }

    /// <summary>
    /// Maps a stored config to the admin DTO.
    /// </summary>
    public static AdminProviderDto FromConfig(OidcProviderConfig config)
    {
        return new AdminProviderDto
        {
            Slug = config.Slug,
            Protocol = config.Protocol,
            Profile = config.Profile,
            AdditionalAuthorizationParameters = config.AdditionalAuthorizationParameters,
            DisplayName = config.DisplayName,
            ButtonText = config.ButtonText,
            Enabled = config.Enabled,
            DisplayOrder = config.DisplayOrder,
            IconUrl = config.IconUrl,
            Authority = config.Authority,
            DiscoveryUrl = config.DiscoveryUrl,
            Issuer = config.Issuer,
            ClientId = config.ClientId,
            HasClientSecret = !string.IsNullOrWhiteSpace(config.ClientSecret),
            Scopes = config.Scopes,
            UsePkce = config.UsePkce,
            FetchUserInfo = config.FetchUserInfo,
            ResponseMode = config.ResponseMode,
            TokenEndpointAuthMethod = config.TokenEndpointAuthMethod,
            CallbackUrlOverride = config.CallbackUrlOverride,
            UsernameClaim = config.UsernameClaim,
            DisplayNameClaim = config.DisplayNameClaim,
            EmailClaim = config.EmailClaim,
            GroupClaimPaths = config.GroupClaimPaths,
            RequiredRoles = config.RequiredRoles,
            AdminRoles = config.AdminRoles,
            RequireVerifiedEmail = config.RequireVerifiedEmail,
            AllowedEmailDomains = config.AllowedEmailDomains,
            UsernameConflictPolicy = config.UsernameConflictPolicy,
            AutoProvision = config.AutoProvision,
            RequireApproval = config.RequireApproval,
            SyncRolesOnLogin = config.SyncRolesOnLogin,
            PublicBaseUrlOverride = config.PublicBaseUrlOverride,
            AllowHttp = config.AllowHttp,
            AllowSymmetricSigning = config.AllowSymmetricSigning,
            AllowAdminProvisioning = config.AllowAdminProvisioning
        };
    }

    /// <summary>
    /// Applies the DTO to a stored config. An empty secret preserves the existing secret.
    /// </summary>
    public void ApplyTo(OidcProviderConfig config)
    {
        config.Protocol = string.IsNullOrWhiteSpace(Protocol) ? "oidc" : Protocol.Trim().ToLowerInvariant();
        config.Profile = string.IsNullOrWhiteSpace(Profile) ? "generic" : Profile.Trim().ToLowerInvariant();
        config.AdditionalAuthorizationParameters = AdditionalAuthorizationParameters ?? [];
        config.DisplayName = DisplayName.Trim();
        config.ButtonText = ButtonText.Trim();
        config.Enabled = Enabled;
        config.DisplayOrder = DisplayOrder;
        config.IconUrl = IconUrl.Trim();
        config.Authority = Authority.Trim();
        config.DiscoveryUrl = DiscoveryUrl.Trim();
        config.Issuer = Issuer.Trim();
        config.ClientId = ClientId.Trim();
        config.Scopes = Scopes.Trim();
        config.UsePkce = UsePkce;
        config.FetchUserInfo = FetchUserInfo;
        config.ResponseMode = ResponseMode;
        config.TokenEndpointAuthMethod = TokenEndpointAuthMethod;
        config.CallbackUrlOverride = CallbackUrlOverride.Trim();
        config.UsernameClaim = UsernameClaim.Trim();
        config.DisplayNameClaim = DisplayNameClaim.Trim();
        config.EmailClaim = EmailClaim.Trim();
        config.GroupClaimPaths = GroupClaimPaths ?? ["groups"];
        config.RequiredRoles = RequiredRoles ?? [];
        config.AdminRoles = AdminRoles ?? [];
        config.RequireVerifiedEmail = RequireVerifiedEmail;
        config.AllowedEmailDomains = AllowedEmailDomains ?? [];
        config.UsernameConflictPolicy = UsernameConflictPolicy;
        config.AutoProvision = AutoProvision;
        config.RequireApproval = RequireApproval;
        config.SyncRolesOnLogin = SyncRolesOnLogin;
        config.PublicBaseUrlOverride = PublicBaseUrlOverride.Trim();
        config.AllowHttp = AllowHttp;
        config.AllowSymmetricSigning = AllowSymmetricSigning;
        config.AllowAdminProvisioning = AllowAdminProvisioning;

        if (ClearClientSecret)
        {
            config.ClientSecret = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(ClientSecret))
        {
            config.ClientSecret = ClientSecret;
        }
    }
}

/// <summary>
/// Global admin settings.
/// </summary>
public sealed class AdminSettingsDto
{
    /// <summary>
    /// Gets or sets the public base URL.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether HTTPS is required.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Gets or sets the state lifetime.
    /// </summary>
    public int StateLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// Gets or sets the ticket lifetime.
    /// </summary>
    public int TicketLifetimeSeconds { get; set; } = 90;

    /// <summary>
    /// Gets or sets provisioning settings.
    /// </summary>
    public ProvisioningSettings Provisioning { get; set; } = new();

    /// <summary>
    /// Gets or sets role settings.
    /// </summary>
    public RoleMappingSettings Roles { get; set; } = new();

    /// <summary>
    /// Gets or sets login page settings.
    /// </summary>
    public LoginPageSettings LoginPage { get; set; } = new();
}

/// <summary>
/// Admin status.
/// </summary>
public sealed class AdminStatusDto
{
    /// <summary>
    /// Gets or sets the plugin version.
    /// </summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin version.
    /// </summary>
    public string JellyfinVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the detected public base URL.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether web injection is enabled.
    /// </summary>
    public bool InjectionEnabled { get; set; }

    /// <summary>
    /// Gets or sets the number of enabled providers.
    /// </summary>
    public int EnabledProviders { get; set; }

    /// <summary>
    /// Gets or sets the number of links.
    /// </summary>
    public int LinkCount { get; set; }

    /// <summary>
    /// Gets or sets the number of pending requests.
    /// </summary>
    public int PendingCount { get; set; }
}

/// <summary>
/// Minimal user list entry for linking.
/// </summary>
public sealed class AdminUserDto
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Pending approval request.
/// </summary>
public sealed class ApprovePendingRequest
{
    /// <summary>
    /// Gets or sets the existing Jellyfin user ID, or null to create one.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to create a new Jellyfin user.
    /// </summary>
    public bool CreateUser { get; set; }
}
