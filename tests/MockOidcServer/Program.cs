using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string Issuer = "http://host.docker.internal:19090";
const string ClientId = "jellyfin";
const string ClientSecret = "test-secret";
var rsa = RSA.Create(2048);
var keyId = "mock-key-1";
var signingKey = new RsaSecurityKey(rsa) { KeyId = keyId };
var codes = new ConcurrentDictionary<string, CodeRecord>();
var accessTokens = new ConcurrentDictionary<string, string>();
var defaultUser = Environment.GetEnvironmentVariable("MOCK_OIDC_USER") ?? "alice";
var defaultGroups = (Environment.GetEnvironmentVariable("MOCK_OIDC_GROUPS") ?? "jellyfin-users")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
{
    ["issuer"] = Issuer,
    ["authorization_endpoint"] = Issuer + "/authorize",
    ["token_endpoint"] = Issuer + "/token",
    ["userinfo_endpoint"] = Issuer + "/userinfo",
    ["jwks_uri"] = Issuer + "/jwks",
    ["end_session_endpoint"] = Issuer + "/end-session",
    ["response_types_supported"] = new[] { "code" },
    ["subject_types_supported"] = new[] { "public" },
    ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
    ["scopes_supported"] = new[] { "openid", "profile", "email", "groups" },
    ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post", "client_secret_basic" }
}));

app.MapGet("/jwks", () =>
{    var parameters = rsa.ExportParameters(false);
    return Results.Json(new
    {
        keys = new[]
        {
            new
            {
                kty = "RSA",
                use = "sig",
                kid = keyId,
                alg = "RS256",
                n = Base64UrlEncoder.Encode(parameters.Modulus),
                e = Base64UrlEncoder.Encode(parameters.Exponent)
            }
        }
    });
});

app.MapGet("/authorize", (HttpRequest request) =>
{
    var query = request.Query;
    var redirectUri = query["redirect_uri"].ToString();
    var state = query["state"].ToString();
    var nonce = query["nonce"].ToString();
    var challenge = query["code_challenge"].ToString();
    var user = query["user"].ToString();
    if (string.IsNullOrWhiteSpace(user))
    {
        user = defaultUser;
    }

    if (string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(state))
    {
        return Results.BadRequest("redirect_uri and state are required");
    }

    var code = Guid.NewGuid().ToString("N");
    codes[code] = new CodeRecord(user, nonce, challenge, redirectUri);
    var target = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
    {
        ["code"] = code,
        ["state"] = state
    });
    return Results.Redirect(target);
});

app.MapPost("/token", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var code = form["code"].ToString();
    var verifier = form["code_verifier"].ToString();
    var redirectUri = form["redirect_uri"].ToString();
    var clientId = form["client_id"].ToString();
    var clientSecret = form["client_secret"].ToString();

    if (request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.ToString()[6..]));
        var parts = raw.Split(':', 2);
        clientId = Uri.UnescapeDataString(parts[0]);
        clientSecret = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
    }

    if (clientId != ClientId || clientSecret != ClientSecret)
    {
        return Results.Json(new { error = "invalid_client" }, statusCode: 401);
    }

    if (!codes.TryRemove(code, out var record) || record.RedirectUri != redirectUri)
    {
        return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
    }

    if (!string.IsNullOrEmpty(record.CodeChallenge))
    {
        var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(record.CodeChallenge)))
        {
            return Results.Json(new { error = "invalid_grant", error_description = "PKCE verification failed" }, statusCode: 400);
        }
    }

    var accessToken = Guid.NewGuid().ToString("N");
    accessTokens[accessToken] = record.User;
    var idToken = CreateIdToken(record.User, record.Nonce);
    return Results.Json(new
    {
        access_token = accessToken,
        token_type = "Bearer",
        expires_in = 3600,
        id_token = idToken
    });
});

app.MapGet("/userinfo", (HttpRequest request) =>
{
    var header = request.Headers.Authorization.ToString();
    var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : string.Empty;
    if (!accessTokens.TryGetValue(token, out var user))
    {
        return Results.Unauthorized();
    }

    return Results.Json(new Dictionary<string, object>
    {
        ["sub"] = SubjectFor(user),
        ["preferred_username"] = user,
        ["name"] = DisplayName(user),
        ["email"] = user + "@example.com",
        ["email_verified"] = true,
        ["groups"] = GroupsFor(user)
    });
});

app.MapGet("/end-session", (HttpRequest request) =>
{
    var redirect = request.Query["post_logout_redirect_uri"].ToString();
    return string.IsNullOrWhiteSpace(redirect)
        ? Results.Ok(new { signedOut = true })
        : Results.Redirect(redirect);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", defaultUser }));

app.MapGet("/set-user", (string name) =>
{
    defaultUser = name;
    return Results.Ok(new { defaultUser });
});

app.Run("http://0.0.0.0:19090");

string CreateIdToken(string user, string? nonce)
{
    var claims = new List<Claim>
    {
        new("sub", SubjectFor(user)),
        new("preferred_username", user),
        new("name", DisplayName(user)),
        new("email", user + "@example.com"),
        new("email_verified", "true")
    };

    if (!string.IsNullOrWhiteSpace(nonce))
    {
        claims.Add(new Claim("nonce", nonce));
    }

    claims.Add(new Claim("groups", JsonSerializer.Serialize(GroupsFor(user))));
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
    var token = new JwtSecurityToken(
        issuer: Issuer,
        audience: ClientId,
        claims: claims,
        notBefore: DateTime.UtcNow.AddMinutes(-1),
        expires: DateTime.UtcNow.AddMinutes(5),
        signingCredentials: credentials);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

string[] GroupsFor(string user)
{
    if (string.Equals(user, "admin", StringComparison.OrdinalIgnoreCase))
    {
        return defaultGroups.Concat(new[] { "jellyfin-admins" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    if (string.Equals(user, "bob", StringComparison.OrdinalIgnoreCase))
    {
        return ["untrusted"];
    }

    return defaultGroups;
}

static string SubjectFor(string user) => "mock-" + user.ToLowerInvariant();
static string DisplayName(string user) => char.ToUpperInvariant(user[0]) + user[1..];
sealed record CodeRecord(string User, string Nonce, string CodeChallenge, string RedirectUri);
