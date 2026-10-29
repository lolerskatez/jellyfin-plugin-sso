using System.Collections.Concurrent;
using System.Text.Json;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Creates and caches provider clients. Discovery and JWKS are cached per provider instance.
/// </summary>
public sealed class OidcProviderFactory : IOidcProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClaimExtractor _claimExtractor;
    private readonly IPluginConfigAccessor _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, Entry> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcProviderFactory"/> class.
    /// </summary>
    public OidcProviderFactory(
        IHttpClientFactory httpClientFactory,
        ClaimExtractor claimExtractor,
        IPluginConfigAccessor config,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _claimExtractor = claimExtractor;
        _config = config;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Gets a provider client, rebuilding it if its configuration changed.
    /// </summary>
    public OidcProvider Get(OidcProviderConfig providerConfig)
    {
        var signature = JsonSerializer.Serialize(providerConfig) + "|" + _config.Current.RequireHttps;
        var entry = _providers.GetOrAdd(providerConfig.Slug, _ => Create(providerConfig, signature));

        if (!string.Equals(entry.Signature, signature, StringComparison.Ordinal))
        {
            entry = Create(providerConfig, signature);
            _providers[providerConfig.Slug] = entry;
        }

        return entry.Provider;
    }

    private Entry Create(OidcProviderConfig config, string signature)
    {
        var client = _httpClientFactory.CreateClient("sso");
        var allowHttp = !_config.Current.RequireHttps;
        var discovery = new OidcDiscoveryService(
            config,
            client,
            _loggerFactory.CreateLogger($"Jellyfin.Plugin.SSO.Oidc.Discovery.{config.Slug}"),
            allowHttp);
        var tokenClient = new OidcTokenClient(
            config,
            client,
            _loggerFactory.CreateLogger($"Jellyfin.Plugin.SSO.Oidc.Token.{config.Slug}"),
            allowHttp);
        var validator = new OidcTokenValidator(
            config,
            _loggerFactory.CreateLogger($"Jellyfin.Plugin.SSO.Oidc.Validation.{config.Slug}"));
        var provider = new OidcProvider(config, discovery, tokenClient, validator, _claimExtractor);

        return new Entry(signature, provider);
    }

    private sealed record Entry(string Signature, OidcProvider Provider);
}
