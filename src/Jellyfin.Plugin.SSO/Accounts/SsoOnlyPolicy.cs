using Jellyfin.Plugin.SSO.Configuration;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// Result of evaluating the password-login policy for one user.
/// </summary>
public sealed class SsoOnlyDecision
{
    /// <summary>
    /// Gets a value indicating whether password login is allowed.
    /// </summary>
    public bool Allowed { get; init; }

    /// <summary>
    /// Gets a short machine-readable reason.
    /// </summary>
    public string Reason { get; init; } = "allowed";

    /// <summary>
    /// Gets a value indicating whether the user has any SSO link.
    /// </summary>
    public bool Linked { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user has a provisioned SSO link.
    /// </summary>
    public bool Provisioned { get; init; }
}

/// <summary>
/// Shared password-login policy used by middleware and the admin API.
/// </summary>
public static class SsoOnlyPolicy
{
    /// <summary>
    /// Evaluates the policy for a Jellyfin user.
    /// </summary>
    public static SsoOnlyDecision Evaluate(
        Guid userId,
        IReadOnlyList<ExternalLinkRecord>? links,
        SsoOnlySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var userLinks = links ?? [];
        var linked = userLinks.Count > 0;
        var provisioned = userLinks.Any(x => x.Provisioned);

        if (!settings.Enabled)
        {
            return new SsoOnlyDecision
            {
                Allowed = true,
                Reason = "disabled",
                Linked = linked,
                Provisioned = provisioned
            };
        }

        if (settings.PasswordLoginAllowedUserIds.Contains(userId))
        {
            return new SsoOnlyDecision
            {
                Allowed = true,
                Reason = "allowlist",
                Linked = linked,
                Provisioned = provisioned
            };
        }

        if (provisioned && settings.BlockProvisionedUsers)
        {
            return new SsoOnlyDecision
            {
                Allowed = false,
                Reason = "sso_only_provisioned",
                Linked = linked,
                Provisioned = provisioned
            };
        }

        if (linked && settings.BlockLinkedUsers)
        {
            return new SsoOnlyDecision
            {
                Allowed = false,
                Reason = "sso_only_linked",
                Linked = linked,
                Provisioned = provisioned
            };
        }

        return new SsoOnlyDecision
        {
            Allowed = true,
            Reason = linked ? "allowed_linked" : "allowed_local",
            Linked = linked,
            Provisioned = provisioned
        };
    }
}
