using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Facade for one provider. Discovery, token exchange, and validation are
/// implemented as separate services so additional protocols can reuse them.
/// </summary>
public sealed class OidcProvider
{
    private static readonly TimeSpan JwksRefreshCooldown = TimeSpan.FromSeconds(30);

    private readonly OidcProviderConfig _config;
    private readonly IOidcDiscoveryService _discovery;
    private readonly IOidcTokenClient _tokenClient;
    private readonly IOidcTokenValidator _tokenValidator;
    private readonly ClaimExtractor _claimExtractor;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcProvider"/> class.
    /// </summary>
    public OidcProvider(
        OidcProviderConfig config,
        IOidcDiscoveryService discovery,
        IOidcTokenClient tokenClient,
        IOidcTokenValidator tokenValidator,
        ClaimExtractor claimExtractor)
    {
        _config = config;
        _discovery = discovery;
        _tokenClient = tokenClient;
        _tokenValidator = tokenValidator;
        _claimExtractor = claimExtractor;
    }

    /// <summary>
    /// Gets the provider configuration.
    /// </summary>
    public OidcProviderConfig Config => _config;

    /// <summary>
    /// Gets the discovery document.
    /// </summary>
    public Task<OidcDiscoveryDocument> GetDiscoveryAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        return _discovery.GetDiscoveryAsync(forceRefresh, cancellationToken);
    }

    /// <summary>
    /// Builds the authorization URL.
    /// </summary>
    public async Task<string> BuildAuthorizationUrlAsync(
        string redirectUri,
        string state,
        string nonce,
        string? codeVerifier,
        CancellationToken cancellationToken = default)
    {
        var discovery = await _discovery.GetDiscoveryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        OidcHttp.EnsureAllowedScheme(discovery.AuthorizationEndpoint, _config, _discovery.AllowHttp);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _config.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = _config.Scopes,
            ["state"] = state,
            ["nonce"] = nonce
        };

        if (!string.IsNullOrWhiteSpace(_config.ResponseMode))
        {
            query["response_mode"] = _config.ResponseMode;
        }

        foreach (var parameter in _config.AdditionalAuthorizationParameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(parameter.Name)
                || query.ContainsKey(parameter.Name))
            {
                continue;
            }

            query[parameter.Name] = parameter.Value;
        }

        if (_config.UsePkce && !string.IsNullOrEmpty(codeVerifier))
        {
            query["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            query["code_challenge_method"] = "S256";
        }

        var separator = discovery.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return discovery.AuthorizationEndpoint + separator
            + string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    /// <summary>
    /// Exchanges an authorization code.
    /// </summary>
    public async Task<OidcTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? codeVerifier,
        CancellationToken cancellationToken = default)
    {
        var discovery = await _discovery.GetDiscoveryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await _tokenClient.ExchangeCodeAsync(
            code,
            redirectUri,
            codeVerifier,
            discovery,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates an ID token and returns its claims. The JWKS is refreshed once
    /// when validation fails, to handle provider key rotation.
    /// </summary>
    public async Task<ClaimSet> ValidateIdTokenAsync(
        string idToken,
        string expectedNonce,
        CancellationToken cancellationToken = default)
    {
        var discovery = await _discovery.GetDiscoveryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var keys = await _discovery.GetSigningKeysAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            return await _tokenValidator.ValidateAsync(idToken, expectedNonce, discovery, keys, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OidcException) when (DateTimeOffset.UtcNow - _discovery.LastJwksFetch > JwksRefreshCooldown)
        {
            var refreshed = await _discovery.GetSigningKeysAsync(true, cancellationToken).ConfigureAwait(false);
            return await _tokenValidator.ValidateAsync(idToken, expectedNonce, discovery, refreshed, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetches UserInfo claims.
    /// </summary>
    public async Task<ClaimSet?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var discovery = await _discovery.GetDiscoveryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await _tokenClient.GetUserInfoAsync(accessToken, discovery, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts the normalized identity from an ID token and optional UserInfo document.
    /// </summary>
    public OidcIdentity ExtractIdentity(ClaimSet tokenClaims, ClaimSet? userInfoClaims)
    {
        var identity = _claimExtractor.Extract(tokenClaims, userInfoClaims, _config);

        if (userInfoClaims is not null)
        {
            var userInfoSubject = userInfoClaims.GetString("sub");
            if (!string.IsNullOrWhiteSpace(userInfoSubject)
                && !string.Equals(userInfoSubject, identity.Subject, StringComparison.Ordinal))
            {
                throw new OidcException("The UserInfo subject did not match the identity token subject.");
            }
        }

        return identity;
    }
}
