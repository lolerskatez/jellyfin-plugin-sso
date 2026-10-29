using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// One in-progress authorization request.
/// </summary>
public sealed class AuthorizationState
{
    /// <summary>
    /// Gets or sets the state parameter sent to the IdP.
    /// </summary>
    public string StateId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the browser binding value stored in the state cookie.
    /// </summary>
    public string BrowserBinding { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the nonce.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PKCE code verifier.
    /// </summary>
    public string CodeVerifier { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exact redirect URI used in the authorization request.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mode: login or link.
    /// </summary>
    public string Mode { get; set; } = "login";

    /// <summary>
    /// Gets or sets the Jellyfin user ID for link mode.
    /// </summary>
    public Guid? JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the validated post-login return URL.
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the expiry time.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// In-memory store for short-lived authorization state.
/// </summary>
public sealed class AuthorizationStateStore
{
    private const int MaxEntries = 5000;
    private readonly ConcurrentDictionary<string, AuthorizationState> _states = new(StringComparer.Ordinal);
    private readonly IPluginConfigAccessor _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationStateStore"/> class.
    /// </summary>
    public AuthorizationStateStore(IPluginConfigAccessor config)
    {
        _config = config;
    }

    /// <summary>
    /// Creates a new authorization state.
    /// </summary>
    public AuthorizationState Create(
        string providerId,
        string redirectUri,
        string mode,
        Guid? jellyfinUserId,
        string returnUrl)
    {
        Cleanup();

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(_config.Current.StateLifetimeMinutes, 2, 30));
        var state = new AuthorizationState
        {
            StateId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
            BrowserBinding = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
            ProviderId = providerId,
            Nonce = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
            CodeVerifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48)),
            RedirectUri = redirectUri,
            Mode = mode,
            JellyfinUserId = jellyfinUserId,
            ReturnUrl = returnUrl,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime)
        };

        _states[state.StateId] = state;

        if (_states.Count > MaxEntries)
        {
            Cleanup(force: true);
        }

        return state;
    }

    /// <summary>
    /// Consumes a state after validating its browser binding.
    /// </summary>
    public AuthorizationState? Consume(string stateId, string? browserBinding)
    {
        if (string.IsNullOrWhiteSpace(stateId)
            || !_states.TryGetValue(stateId, out var state))
        {
            return null;
        }

        // Always remove the state so a failed callback cannot be replayed.
        _states.TryRemove(stateId, out _);

        if (state.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(browserBinding)
            || !FixedTimeEquals(state.BrowserBinding, browserBinding))
        {
            return null;
        }

        return state;
    }

    /// <summary>
    /// Gets the cookie name used to bind a state to the browser.
    /// </summary>
    public static string GetCookieName(string stateId)
    {
        var prefix = stateId.Length >= 8 ? stateId[..8] : stateId;
        return "jf-sso-state-" + prefix;
    }

    private void Cleanup(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _states)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _states.TryRemove(pair.Key, out _);
            }
        }

        if (force && _states.Count > MaxEntries)
        {
            foreach (var pair in _states.OrderBy(x => x.Value.CreatedAt).Take(_states.Count - MaxEntries))
            {
                _states.TryRemove(pair.Key, out _);
            }
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
