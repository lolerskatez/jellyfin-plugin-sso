using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Jellyfin.Plugin.SSO.Web;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Jellyfin.Plugin.SSO.Tests;

public class ClaimSetTests
{
    [Fact]
    public void ReadsNestedGroups()
    {
        using var document = JsonDocument.Parse("""
        {
          "realm_access": { "roles": ["jellyfin-users", "jellyfin-admins"] },
          "groups": "one two",
          "email_verified": true
        }
        """);

        var claims = new ClaimSet(document.RootElement);

        Assert.Equal(["jellyfin-users", "jellyfin-admins"], claims.GetStrings("realm_access.roles"));
        Assert.Equal(["one two"], claims.GetStrings("groups"));
        Assert.True(claims.GetBool("email_verified"));
    }

    [Fact]
    public void ReadsEscapedDotInClaimName()
    {
        using var document = JsonDocument.Parse("""{ "a.b": "value" }""");
        var claims = new ClaimSet(document.RootElement);
        Assert.Equal("value", claims.GetString("a\\.b"));
    }
}

public class UsernameResolverTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("alice@example.com", "alice@example.com")]
    [InlineData("  Alice Smith  ", "Alice Smith")]
    [InlineData("bad/name", "bad-name")]
    [InlineData(".", "")]
    [InlineData("..", "")]
    public void SanitizesUsernames(string input, string expected)
    {
        Assert.Equal(expected, UsernameResolver.Sanitize(input));
    }
}

public class SsoUrlBuilderTests
{
    [Theory]
    [InlineData(null, "/web/index.html#/home")]
    [InlineData("", "/web/index.html#/home")]
    [InlineData("/home", "/web/index.html#/home")]
    [InlineData("/web/index.html#/home", "/web/index.html#/home")]
    [InlineData("/sso/quickconnect", "/sso/quickconnect")]
    [InlineData("https://evil.example/", "/web/index.html#/home")]
    [InlineData("//evil.example/", "/web/index.html#/home")]
    public void NormalizesReturnUrls(string? input, string expected)
    {
        Assert.Equal(expected, SsoUrlBuilder.NormalizeReturnUrl(input));
    }
}

public class OidcTokenValidatorTests
{
    [Fact]
    public async Task ValidatesSignatureIssuerAudienceAndNonce()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://issuer.example",
            Audience = "jellyfin",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "alice"),
                new Claim("nonce", "expected-nonce")
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });

        var validator = new OidcTokenValidator(
            new OidcProviderConfig { ClientId = "jellyfin", Issuer = "https://issuer.example" },
            NullLogger<OidcTokenValidator>.Instance);
        var discovery = new OidcDiscoveryDocument { Issuer = "https://issuer.example" };

        var claims = await validator.ValidateAsync(token, "expected-nonce", discovery, [key]);

        Assert.Equal("alice", claims.GetString("sub"));
    }

    [Fact]
    public async Task RejectsMismatchedNonce()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://issuer.example",
            Audience = "jellyfin",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "alice"),
                new Claim("nonce", "different")
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });

        var validator = new OidcTokenValidator(
            new OidcProviderConfig { ClientId = "jellyfin", Issuer = "https://issuer.example" },
            NullLogger<OidcTokenValidator>.Instance);
        var discovery = new OidcDiscoveryDocument { Issuer = "https://issuer.example" };

        await Assert.ThrowsAsync<OidcException>(() =>
            validator.ValidateAsync(token, "expected-nonce", discovery, [key]));
    }
}

public class PluginConfigurationMigratorTests
{
    [Fact]
    public void MigratesLegacyConfigurationAndNormalizesProviderDefaults()
    {
        var configuration = new PluginConfiguration
        {
            SchemaVersion = 0,
            Providers =
            [
                new OidcProviderConfig
                {
                    Slug = "authentik",
                    Protocol = null!,
                    Profile = null!,
                    GroupClaimPaths = null!,
                    RequiredRoles = null!,
                    AdminRoles = null!,
                    AllowedEmailDomains = null!,
                    AdditionalAuthorizationParameters = null!
                }
            ]
        };

        Assert.True(PluginConfigurationMigrator.Migrate(configuration));
        Assert.Equal(PluginConfigurationMigrator.CurrentVersion, configuration.SchemaVersion);
        Assert.Equal("oidc", configuration.Providers[0].Protocol);
        Assert.Equal("generic", configuration.Providers[0].Profile);
        Assert.NotNull(configuration.Providers[0].GroupClaimPaths);
        Assert.NotNull(configuration.Providers[0].AdditionalAuthorizationParameters);
        Assert.False(PluginConfigurationMigrator.Migrate(configuration));
    }
}

public class AuthorizationStateStoreTests
{
    [Fact]
    public void ConsumesStateOnceAndRequiresBinding()
    {
        var store = new AuthorizationStateStore(new FakeConfigAccessor());
        var state = store.Create("authentik", "https://jellyfin.example/sso/callback/authentik", "login", null, "/web/");

        Assert.Null(store.Consume(state.StateId, "wrong"));
        Assert.Null(store.Consume(state.StateId, state.BrowserBinding));
    }

    [Fact]
    public void SuccessfulConsumeReturnsState()
    {
        var store = new AuthorizationStateStore(new FakeConfigAccessor());
        var state = store.Create("authentik", "https://jellyfin.example/sso/callback/authentik", "login", null, "/web/");

        var consumed = store.Consume(state.StateId, state.BrowserBinding);

        Assert.NotNull(consumed);
        Assert.Equal("authentik", consumed!.ProviderId);
        Assert.Null(store.Consume(state.StateId, state.BrowserBinding));
    }
}

public class LoginTicketStoreTests
{
    [Fact]
    public void ConsumesTicketOnceAndRequiresBinding()
    {
        var store = new LoginTicketStore(new FakeConfigAccessor());
        var (ticket, binding) = store.Create(Guid.NewGuid(), "authentik", "/web/");

        Assert.Null(store.Consume(ticket, "wrong"));
        Assert.Null(store.Consume(ticket, binding));
    }

    [Fact]
    public void SuccessfulTicketConsumeReturnsRecord()
    {
        var store = new LoginTicketStore(new FakeConfigAccessor());
        var userId = Guid.NewGuid();
        var (ticket, binding) = store.Create(userId, "authentik", "/web/");

        var consumed = store.Consume(ticket, binding);

        Assert.NotNull(consumed);
        Assert.Equal(userId, consumed!.JellyfinUserId);
        Assert.Null(store.Consume(ticket, binding));
    }
}

internal sealed class FakeConfigAccessor : IPluginConfigAccessor
{
    public PluginConfiguration Current { get; } = new();

    public void Save()
    {
    }
}
