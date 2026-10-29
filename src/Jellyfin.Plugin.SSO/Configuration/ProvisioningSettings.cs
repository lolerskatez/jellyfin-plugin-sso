namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Settings that control automatic Jellyfin user creation.
/// </summary>
public class ProvisioningSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether unknown SSO identities create Jellyfin users.
    /// </summary>
    public bool AutoProvisionNewUsers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new users must be approved by an administrator.
    /// </summary>
    public bool RequireAdminApproval { get; set; }

    /// <summary>
    /// Gets or sets an optional Jellyfin user whose policy is cloned for new users.
    /// </summary>
    public Guid? PolicyTemplateUserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether newly provisioned users are hidden from the login user picker.
    /// </summary>
    public bool HideNewUsers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new users get permission to download media.
    /// </summary>
    public bool AllowDownloads { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new users get remote access.
    /// </summary>
    public bool AllowRemoteAccess { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new users get access to all libraries.
    /// </summary>
    public bool AllowAllFolders { get; set; } = true;

    /// <summary>
    /// Gets or sets the library folder IDs granted when AllowAllFolders is false.
    /// </summary>
    public List<Guid> DefaultEnabledFolders { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether new users get Live TV access by default.
    /// </summary>
    public bool AllowLiveTv { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a new user may be created as an administrator from an IdP claim.
    /// </summary>
    public bool AllowAdminProvisioning { get; set; }

    /// <summary>
    /// Gets or sets the default username collision policy: Pending, Reject, or Suffix.
    /// </summary>
    public string UsernameConflictPolicy { get; set; } = "Pending";

    /// <summary>
    /// Gets or sets a value indicating whether role mappings are applied on every login.
    /// </summary>
    public bool SyncRolesOnEveryLogin { get; set; }

    /// <summary>
    /// Gets or sets the behavior when a mapped claim is missing: Keep, Clear, or Deny.
    /// </summary>
    public string MissingRoleBehavior { get; set; } = "Keep";

    /// <summary>
    /// Gets or sets a value indicating whether active tokens are revoked when a link is removed.
    /// </summary>
    public bool RevokeTokensOnUnlink { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pending requests accepted per IP per hour.
    /// </summary>
    public int MaxPendingRequestsPerIpPerHour { get; set; } = 10;
}
