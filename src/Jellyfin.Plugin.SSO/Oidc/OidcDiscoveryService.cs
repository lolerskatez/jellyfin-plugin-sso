using System.Text.Json;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Fetches and caches OIDC discovery documents and signing keys.
/// </summary>
public sealed class OidcDiscoveryService : IOidcDiscoveryService
{
    private static readonly TimeSpan DiscoveryLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan JwksLifetime = TimeSpan.FromHours(1);

    private readonly OidcProviderConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly bool _allowHttp;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly SemaphoreSlim _jwksGate = new(1, 1);

    private OidcDiscoveryDocument? _discovery;
    private DateTimeOffset _discoveryFetchedAt;
    private JsonWebKeySet? _jwks;
    private DateTimeOffset _jwksFetchedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcDiscoveryService"/> class.
    /// </summary>
    public OidcDiscoveryService(
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
    /// Gets the provider configuration.
    /// </summary>
    public OidcProviderConfig Config => _config;

    /// <summary>
    /// Gets a value indicating whether HTTP is allowed.
    /// </summary>
    public bool AllowHttp => _allowHttp;

    /// <summary>
    /// Gets when the JWKS was last fetched.
    /// </summary>
    public DateTimeOffset LastJwksFetch => _jwksFetchedAt;

    /// <summary>
    /// Gets the discovery document.
    /// </summary>
    public async Task<OidcDiscoveryDocument> GetDiscoveryAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _discovery is not null && DateTimeOffset.UtcNow - _discoveryFetchedAt < DiscoveryLifetime)
        {
            return _discovery;
        }

        await _discoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _discovery is not null && DateTimeOffset.UtcNow - _discoveryFetchedAt < DiscoveryLifetime)
            {
                return _discovery;
            }

            var url = GetDiscoveryUri();
            OidcHttp.EnsureAllowedScheme(url, _config, _allowHttp);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await OidcHttp.ReadLimitedStringAsync(response, cancellationToken).ConfigureAwait(false);
            var discovery = JsonSerializer.Deserialize<OidcDiscoveryDocument>(json, JsonDefaults.Options)
                ?? throw new OidcException("The OIDC discovery document was empty.");

            if (string.IsNullOrWhiteSpace(discovery.Issuer)
                || string.IsNullOrWhiteSpace(discovery.AuthorizationEndpoint)
                || string.IsNullOrWhiteSpace(discovery.TokenEndpoint)
                || string.IsNullOrWhiteSpace(discovery.JwksUri))
            {
                throw new OidcException("The OIDC discovery document is missing required endpoints.");
            }

            if (!string.IsNullOrWhiteSpace(_config.Issuer)
                && !string.Equals(_config.Issuer, discovery.Issuer, StringComparison.Ordinal))
            {
                throw new OidcException("The configured issuer does not match the OIDC discovery document.");
            }

            _discovery = discovery;
            _discoveryFetchedAt = DateTimeOffset.UtcNow;
            return discovery;
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    /// <summary>
    /// Gets signing keys, optionally forcing a JWKS refresh.
    /// </summary>
    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _jwks is not null && DateTimeOffset.UtcNow - _jwksFetchedAt < JwksLifetime)
        {
            return _jwks.GetSigningKeys().ToList();
        }

        await _jwksGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _jwks is not null && DateTimeOffset.UtcNow - _jwksFetchedAt < JwksLifetime)
            {
                return _jwks.GetSigningKeys().ToList();
            }

            var discovery = await GetDiscoveryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            OidcHttp.EnsureAllowedScheme(discovery.JwksUri, _config, _allowHttp);

            using var response = await _httpClient.GetAsync(discovery.JwksUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await OidcHttp.ReadLimitedStringAsync(response, cancellationToken).ConfigureAwait(false);
            _jwks = new JsonWebKeySet(json);
            _jwksFetchedAt = DateTimeOffset.UtcNow;

            var keys = _jwks.GetSigningKeys();
            if (keys.Count == 0)
            {
                throw new OidcException("The OIDC provider did not publish any signing keys.");
            }

            return keys.ToList();
        }
        finally
        {
            _jwksGate.Release();
        }
    }

    private string GetDiscoveryUri()
    {
        var authority = _config.Authority.Trim();
        var url = _config.DiscoveryUrl.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            if (authority.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
            {
                url = authority;
            }
            else
            {
                url = authority.TrimEnd('/') + "/.well-known/openid-configuration";
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new OidcException("The OIDC discovery URL is not a valid absolute URL.");
        }

        return uri.ToString();
    }
}
