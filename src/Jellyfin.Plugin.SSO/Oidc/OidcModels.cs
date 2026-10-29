using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// OIDC discovery document.
/// </summary>
public sealed class OidcDiscoveryDocument
{
    /// <summary>
    /// Gets or sets the issuer.
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authorization endpoint.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UserInfo endpoint.
    /// </summary>
    [JsonPropertyName("userinfo_endpoint")]
    public string UserInfoEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JWKS endpoint.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string JwksUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the end-session endpoint.
    /// </summary>
    [JsonPropertyName("end_session_endpoint")]
    public string EndSessionEndpoint { get; set; } = string.Empty;
}

/// <summary>
/// Token endpoint response.
/// </summary>
public sealed class OidcTokenResponse
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ID token.
    /// </summary>
    [JsonPropertyName("id_token")]
    public string IdToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token type.
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expiry in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Expected OIDC failure.
/// </summary>
public sealed class OidcException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OidcException"/> class.
    /// </summary>
    public OidcException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcException"/> class.
    /// </summary>
    public OidcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
