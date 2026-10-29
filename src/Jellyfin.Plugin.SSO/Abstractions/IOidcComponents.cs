using Jellyfin.Plugin.SSO.Oidc;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Provides OIDC discovery and signing keys.
/// </summary>
public interface IOidcDiscoveryService
{
    bool AllowHttp { get; }

    DateTimeOffset LastJwksFetch { get; }

    Task<OidcDiscoveryDocument> GetDiscoveryAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles OIDC token endpoint and UserInfo calls.
/// </summary>
public interface IOidcTokenClient
{
    Task<OidcTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? codeVerifier,
        OidcDiscoveryDocument discovery,
        CancellationToken cancellationToken = default);

    Task<ClaimSet?> GetUserInfoAsync(
        string accessToken,
        OidcDiscoveryDocument discovery,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates OIDC ID tokens.
/// </summary>
public interface IOidcTokenValidator
{
    Task<ClaimSet> ValidateAsync(
        string idToken,
        string expectedNonce,
        OidcDiscoveryDocument discovery,
        IReadOnlyCollection<SecurityKey> signingKeys,
        CancellationToken cancellationToken = default);
}
