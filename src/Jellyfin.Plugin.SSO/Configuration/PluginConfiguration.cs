using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Root plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the configuration schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the configured OIDC providers.
    /// </summary>
    public List<OidcProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets global user provisioning settings.
    /// </summary>
    public ProvisioningSettings Provisioning { get; set; } = new();

    /// <summary>
    /// Gets or sets role mapping settings.
    /// </summary>
    public RoleMappingSettings Roles { get; set; } = new();

    /// <summary>
    /// Gets or sets login page integration settings.
    /// </summary>
    public LoginPageSettings LoginPage { get; set; } = new();

    /// <summary>
    /// Gets or sets password-login policy settings.
    /// </summary>
    public SsoOnlySettings SsoOnly { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether discovery and token endpoints must use HTTPS.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Gets or sets the lifetime of an authorization state in minutes.
    /// </summary>
    public int StateLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// Gets or sets the lifetime of a one-time login ticket in seconds.
    /// </summary>
    public int TicketLifetimeSeconds { get; set; } = 90;

    /// <summary>
    /// Gets or sets an optional public base URL override, for example https://jellyfin.example.com.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether detailed OIDC failure reasons may be written to the log.
    /// </summary>
    public bool LogDetailedErrors { get; set; } = true;
}
