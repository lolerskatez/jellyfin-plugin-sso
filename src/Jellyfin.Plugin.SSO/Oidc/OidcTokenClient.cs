using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Exchanges authorization codes and fetches UserInfo.
/// </summary>
public sealed class OidcTokenClient : IOidcTokenClient
{
    private readonly OidcProviderConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly bool _allowHttp;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcTokenClient"/> class.
    /// </summary>
    public OidcTokenClient(
        OidcProviderConfig config,
        HttpClient httpClient,
        ILogger logger,
        bool allowHttp)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
        _allowHttp = allowHttp;
    }

    /// <summary>
    /// Exchanges an authorization code.
    /// </summary>
    public async Task<OidcTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? codeVerifier,
        OidcDiscoveryDocument discovery,
        CancellationToken cancellationToken = default)
    {
        OidcHttp.EnsureAllowedScheme(discovery.TokenEndpoint, _config, _allowHttp);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _config.ClientId
        };

        if (_config.UsePkce && !string.IsNullOrWhiteSpace(codeVerifier))
        {
            form["code_verifier"] = codeVerifier;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
        {
            if (string.Equals(_config.TokenEndpointAuthMethod, "client_secret_basic", StringComparison.OrdinalIgnoreCase))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(_config.ClientId)}:{Uri.EscapeDataString(_config.ClientSecret)}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
            else if (!string.Equals(_config.TokenEndpointAuthMethod, "none", StringComparison.OrdinalIgnoreCase))
            {
                form["client_secret"] = _config.ClientSecret;
                request.Content = new FormUrlEncodedContent(form);
            }
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var json = await OidcHttp.ReadLimitedStringAsync(response, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OIDC token exchange failed for provider {Provider} with status {Status}: {Body}",
                _config.Slug,
                (int)response.StatusCode,
                OidcHttp.Truncate(json, 512));
            throw new OidcException("The identity provider rejected the token request.");
        }

        var token = JsonSerializer.Deserialize<OidcTokenResponse>(json, JsonDefaults.Options)
            ?? throw new OidcException("The token endpoint returned an empty response.");

        if (string.IsNullOrWhiteSpace(token.IdToken))
        {
            throw new OidcException("The token endpoint did not return an ID token.");
        }

        return token;
    }

    /// <summary>
    /// Fetches UserInfo claims.
    /// </summary>
    public async Task<ClaimSet?> GetUserInfoAsync(
        string accessToken,
        OidcDiscoveryDocument discovery,
        CancellationToken cancellationToken = default)
    {
        if (!_config.FetchUserInfo || string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(discovery.UserInfoEndpoint))
        {
            return null;
        }

        OidcHttp.EnsureAllowedScheme(discovery.UserInfoEndpoint, _config, _allowHttp);

        using var request = new HttpRequestMessage(HttpMethod.Get, discovery.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OIDC UserInfo request failed for provider {Provider} with status {Status}",
                _config.Slug,
                (int)response.StatusCode);
            return null;
        }

        var json = await OidcHttp.ReadLimitedStringAsync(response, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return new ClaimSet(document.RootElement);
    }
}
