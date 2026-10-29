using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Validates OIDC ID tokens.
/// </summary>
public sealed class OidcTokenValidator : IOidcTokenValidator
{
    private readonly OidcProviderConfig _config;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcTokenValidator"/> class.
    /// </summary>
    public OidcTokenValidator(OidcProviderConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Validates an ID token and returns its claims.
    /// </summary>
    public async Task<ClaimSet> ValidateAsync(
        string idToken,
        string expectedNonce,
        OidcDiscoveryDocument discovery,
        IReadOnlyCollection<SecurityKey> signingKeys,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateWithKeysAsync(idToken, discovery, signingKeys, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "OIDC ID token validation failed for provider {Provider}: {Error}",
                _config.Slug,
                validation.Exception?.Message ?? "unknown validation error");
            throw new OidcException("The identity token could not be validated.");
        }

        var jwt = validation.SecurityToken as JsonWebToken
            ?? throw new OidcException("The identity token had an unexpected format.");

        var claims = DecodePayload(jwt.EncodedPayload);
        var nonce = claims.GetString("nonce");
        if (!FixedTimeEquals(nonce, expectedNonce))
        {
            throw new OidcException("The identity token nonce did not match the authorization request.");
        }

        var azp = claims.GetString("azp");
        if (!string.IsNullOrWhiteSpace(azp)
            && !string.Equals(azp, _config.ClientId, StringComparison.Ordinal))
        {
            throw new OidcException("The identity token was issued to a different client.");
        }

        return claims;
    }

    private async Task<TokenValidationResult> ValidateWithKeysAsync(
        string idToken,
        OidcDiscoveryDocument discovery,
        IReadOnlyCollection<SecurityKey> signingKeys,
        CancellationToken cancellationToken)
    {
        var algorithms = _config.AllowSymmetricSigning
            ? new[] { "RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512", "HS256", "HS384", "HS512" }
            : new[] { "RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512" };

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = !string.IsNullOrWhiteSpace(_config.Issuer) ? _config.Issuer : discovery.Issuer,
            ValidateAudience = true,
            ValidAudience = _config.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidAlgorithms = algorithms
        };

        cancellationToken.ThrowIfCancellationRequested();
        var handler = new JsonWebTokenHandler();
        return await handler.ValidateTokenAsync(idToken, parameters).ConfigureAwait(false);
    }

    private static ClaimSet DecodePayload(string encodedPayload)
    {
        var bytes = Base64UrlEncoder.DecodeBytes(encodedPayload);
        using var document = JsonDocument.Parse(bytes);
        return new ClaimSet(document.RootElement);
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
