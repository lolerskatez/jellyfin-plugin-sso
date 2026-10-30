namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Password-login policy settings.
/// </summary>
public sealed class SsoOnlySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether password login policy is enforced.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether provisioned SSO users are blocked from password login.
    /// </summary>
    public bool BlockProvisionedUsers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether users with any SSO link are blocked from password login.
    /// </summary>
    public bool BlockLinkedUsers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether API-key-authenticated password checks are allowed.
    /// </summary>
    public bool AllowApiKeyPasswordVerification { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the local login form is hidden on the web login page.
    /// </summary>
    public bool HideLocalLogin { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the web login page shows a local-account fallback link.
    /// </summary>
    public bool ShowLocalAccountLink { get; set; } = true;

    /// <summary>
    /// Gets or sets Jellyfin user IDs that may always use password login.
    /// </summary>
    public List<Guid> PasswordLoginAllowedUserIds { get; set; } = [];
}
