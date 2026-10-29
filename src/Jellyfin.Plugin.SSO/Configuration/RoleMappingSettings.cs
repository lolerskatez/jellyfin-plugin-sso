namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Role-to-policy mappings shared by all providers.
/// </summary>
public class RoleMappingSettings
{
    /// <summary>
    /// Gets or sets folder access mappings.
    /// </summary>
    public List<FolderRoleMapping> FolderRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets roles that grant Live TV access.
    /// </summary>
    public List<string> LiveTvRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets roles that grant Live TV management.
    /// </summary>
    public List<string> LiveTvManagementRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a mapped role is required for access.
    /// </summary>
    public bool RequireRoleForAccess { get; set; }

    /// <summary>
    /// Gets or sets roles that are required when RequireRoleForAccess is true.
    /// </summary>
    public List<string> RequiredRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the last administrator is protected from demotion.
    /// </summary>
    public bool ProtectLastAdmin { get; set; } = true;
}

/// <summary>
/// Maps one external role to one or more Jellyfin library folders.
/// </summary>
public class FolderRoleMapping
{
    /// <summary>
    /// Gets or sets the external role name.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin library folder IDs.
    /// </summary>
    public List<Guid> FolderIds { get; set; } = [];
}
