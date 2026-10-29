using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// Result of evaluating external groups against configured role mappings.
/// </summary>
public sealed class RoleEvaluation
{
    /// <summary>
    /// Gets or sets a value indicating whether the identity is allowed to access Jellyfin.
    /// </summary>
    public bool HasAccess { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the identity should be an administrator.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether folder role mappings matched.
    /// </summary>
    public bool HasFolderMapping { get; set; }

    /// <summary>
    /// Gets or sets the mapped folder IDs.
    /// </summary>
    public IReadOnlyList<Guid> Folders { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether Live TV access is granted.
    /// </summary>
    public bool LiveTv { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Live TV management is granted.
    /// </summary>
    public bool LiveTvManagement { get; set; }
}

/// <summary>
/// Maps external groups to Jellyfin user policy.
/// </summary>
public sealed class PolicyMapper : IPolicyMapper
{
    private readonly IUserManager _userManager;
    private readonly IPluginConfigAccessor _config;
    private readonly ILogger<PolicyMapper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyMapper"/> class.
    /// </summary>
    public PolicyMapper(IUserManager userManager, IPluginConfigAccessor config, ILogger<PolicyMapper> logger)
    {
        _userManager = userManager;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates external groups.
    /// </summary>
    public RoleEvaluation Evaluate(OidcIdentity identity, OidcProviderConfig provider)
    {
        var config = _config.Current;
        var groups = identity.Groups;

        var required = new List<string>();
        required.AddRange(provider.RequiredRoles);
        if (config.Roles.RequireRoleForAccess)
        {
            required.AddRange(config.Roles.RequiredRoles);
        }

        var hasAccess = required.Count == 0 || MatchesAny(groups, required);
        if (groups.Count == 0
            && string.Equals(config.Provisioning.MissingRoleBehavior, "Deny", StringComparison.OrdinalIgnoreCase)
            && (provider.AdminRoles.Count > 0
                || config.Roles.FolderRoles.Count > 0
                || config.Roles.LiveTvRoles.Count > 0
                || config.Roles.LiveTvManagementRoles.Count > 0))
        {
            hasAccess = false;
        }

        var folders = new List<Guid>();
        var hasFolderMapping = false;
        foreach (var mapping in config.Roles.FolderRoles)
        {
            if (!MatchesAny(groups, [mapping.Role]))
            {
                continue;
            }

            hasFolderMapping = true;
            folders.AddRange(mapping.FolderIds);
        }

        return new RoleEvaluation
        {
            HasAccess = hasAccess,
            IsAdmin = MatchesAny(groups, provider.AdminRoles),
            HasFolderMapping = hasFolderMapping,
            Folders = folders.Distinct().ToArray(),
            LiveTv = MatchesAny(groups, config.Roles.LiveTvRoles),
            LiveTvManagement = MatchesAny(groups, config.Roles.LiveTvManagementRoles)
        };
    }

    /// <summary>
    /// Applies policy to a newly created user.
    /// </summary>
    public async Task ApplyNewUserPolicyAsync(
        User user,
        OidcProviderConfig provider,
        RoleEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        var config = _config.Current;
        var policy = GetTemplatePolicy(config.Provisioning.PolicyTemplateUserId)
            ?? new UserPolicy();

        // A new user must remain on Jellyfin's default auth provider so the random
        // password and any administrator-set local password work normally.
        policy.AuthenticationProviderId = user.AuthenticationProviderId;
        policy.PasswordResetProviderId = user.PasswordResetProviderId;
        policy.IsAdministrator = evaluation.IsAdmin
            && (config.Provisioning.AllowAdminProvisioning || provider.AllowAdminProvisioning);
        policy.IsHidden = config.Provisioning.HideNewUsers;
        policy.IsDisabled = false;
        policy.EnableRemoteAccess = config.Provisioning.AllowRemoteAccess;
        policy.EnableContentDownloading = config.Provisioning.AllowDownloads;
        policy.EnableMediaPlayback = true;
        policy.EnableAudioPlaybackTranscoding = true;
        policy.EnableVideoPlaybackTranscoding = true;
        policy.EnablePlaybackRemuxing = true;

        if (evaluation.HasFolderMapping)
        {
            policy.EnableAllFolders = false;
            policy.EnabledFolders = evaluation.Folders.ToArray();
        }
        else
        {
            policy.EnableAllFolders = config.Provisioning.AllowAllFolders;
            policy.EnabledFolders = config.Provisioning.AllowAllFolders
                ? []
                : config.Provisioning.DefaultEnabledFolders.ToArray();
        }

        policy.EnableLiveTvAccess = evaluation.LiveTv || config.Provisioning.AllowLiveTv;
        policy.EnableLiveTvManagement = evaluation.LiveTvManagement;

        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies role synchronization to an existing user.
    /// </summary>
    public async Task ApplySyncPolicyAsync(
        User user,
        OidcProviderConfig provider,
        RoleEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        var config = _config.Current;
        var policy = _userManager.GetUserDto(user).Policy;
        var changed = false;

        if (provider.AdminRoles.Count > 0 && policy.IsAdministrator != evaluation.IsAdmin)
        {
            if (!evaluation.IsAdmin && config.Roles.ProtectLastAdmin && CountAdministrators() <= 1)
            {
                _logger.LogWarning(
                    "Skipping administrator demotion for {User}: this is the last administrator.",
                    user.Username);
            }
            else
            {
                policy.IsAdministrator = evaluation.IsAdmin;
                changed = true;
            }
        }

        if (evaluation.HasFolderMapping)
        {
            policy.EnableAllFolders = false;
            policy.EnabledFolders = evaluation.Folders.ToArray();
            changed = true;
        }

        if (config.Roles.LiveTvRoles.Count > 0)
        {
            policy.EnableLiveTvAccess = evaluation.LiveTv;
            changed = true;
        }

        if (config.Roles.LiveTvManagementRoles.Count > 0)
        {
            policy.EnableLiveTvManagement = evaluation.LiveTvManagement;
            changed = true;
        }

        if (changed)
        {
            await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets whether role syncing is enabled for a provider.
    /// </summary>
    public bool ShouldSyncRoles(OidcProviderConfig provider)
    {
        return provider.SyncRolesOnLogin ?? _config.Current.Provisioning.SyncRolesOnEveryLogin;
    }

    private UserPolicy? GetTemplatePolicy(Guid? templateUserId)
    {
        if (templateUserId is null)
        {
            return null;
        }

        var template = _userManager.GetUserById(templateUserId.Value);
        if (template is null)
        {
            _logger.LogWarning("Configured policy template user {UserId} no longer exists.", templateUserId);
            return null;
        }

        return _userManager.GetUserDto(template).Policy;
    }

    private int CountAdministrators()
    {
        return _userManager.GetUsers().Count(x => x.HasPermission(PermissionKind.IsAdministrator));
    }

    private static bool MatchesAny(IReadOnlyList<string> groups, IReadOnlyList<string> roles)
    {
        if (roles.Count == 0)
        {
            return false;
        }

        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            if (string.Equals(role, "*", StringComparison.Ordinal))
            {
                return true;
            }

            if (groups.Any(group => string.Equals(group, role, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
