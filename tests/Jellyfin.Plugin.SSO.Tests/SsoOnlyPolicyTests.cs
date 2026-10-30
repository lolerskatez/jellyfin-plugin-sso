using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Xunit;

namespace Jellyfin.Plugin.SSO.Tests;

public class SsoOnlyPolicyTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ExternalLinkRecord Link(bool provisioned, string source = "auto")
    {
        return new ExternalLinkRecord
        {
            ProviderId = "authentik",
            Issuer = "https://issuer.example",
            Subject = "alice",
            JellyfinUserId = UserId,
            Source = source,
            Provisioned = provisioned
        };
    }

    [Fact]
    public void DisabledPolicyAllowsEverything()
    {
        var decision = SsoOnlyPolicy.Evaluate(
            UserId,
            [Link(provisioned: true)],
            new SsoOnlySettings { Enabled = false });

        Assert.True(decision.Allowed);
        Assert.Equal("disabled", decision.Reason);
    }

    [Fact]
    public void ProvisionedUserIsBlockedByDefault()
    {
        var decision = SsoOnlyPolicy.Evaluate(
            UserId,
            [Link(provisioned: true)],
            new SsoOnlySettings { Enabled = true });

        Assert.False(decision.Allowed);
        Assert.Equal("sso_only_provisioned", decision.Reason);
        Assert.True(decision.Provisioned);
    }

    [Fact]
    public void LinkedLocalUserIsAllowedInBalancedMode()
    {
        var decision = SsoOnlyPolicy.Evaluate(
            UserId,
            [Link(provisioned: false, source: "self")],
            new SsoOnlySettings { Enabled = true });

        Assert.True(decision.Allowed);
        Assert.Equal("allowed_linked", decision.Reason);
    }

    [Fact]
    public void LinkedLocalUserIsBlockedInStrictMode()
    {
        var decision = SsoOnlyPolicy.Evaluate(
            UserId,
            [Link(provisioned: false, source: "self")],
            new SsoOnlySettings { Enabled = true, BlockLinkedUsers = true });

        Assert.False(decision.Allowed);
        Assert.Equal("sso_only_linked", decision.Reason);
    }

    [Fact]
    public void AllowlistOverridesProvisionedPolicy()
    {
        var settings = new SsoOnlySettings { Enabled = true };
        settings.PasswordLoginAllowedUserIds.Add(UserId);

        var decision = SsoOnlyPolicy.Evaluate(UserId, [Link(provisioned: true)], settings);

        Assert.True(decision.Allowed);
        Assert.Equal("allowlist", decision.Reason);
    }

    [Fact]
    public void LocalUserIsAllowed()
    {
        var decision = SsoOnlyPolicy.Evaluate(UserId, [], new SsoOnlySettings { Enabled = true });

        Assert.True(decision.Allowed);
        Assert.Equal("allowed_local", decision.Reason);
        Assert.False(decision.Linked);
    }

    [Fact]
    public void ProvisionedCanBeAllowedWhenBlockingIsOff()
    {
        var decision = SsoOnlyPolicy.Evaluate(
            UserId,
            [Link(provisioned: true)],
            new SsoOnlySettings { Enabled = true, BlockProvisionedUsers = false });

        Assert.True(decision.Allowed);
        Assert.Equal("allowed_linked", decision.Reason);
    }
}
