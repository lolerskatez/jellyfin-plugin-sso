using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// A one-time ticket used to exchange an SSO callback for Jellyfin credentials.
/// </summary>
public sealed class LoginTicket
{
    /// <summary>
    /// Gets or sets the ticket hash.
    /// </summary>
    public string TicketHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the browser binding hash.
    /// </summary>
    public string BindingHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin user ID.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the return URL.
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expiry.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// In-memory, one-time login ticket store.
/// </summary>
public sealed class LoginTicketStore
{
    private const int MaxEntries = 5000;
    private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new(StringComparer.Ordinal);
    private readonly IPluginConfigAccessor _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginTicketStore"/> class.
    /// </summary>
    public LoginTicketStore(IPluginConfigAccessor config)
    {
        _config = config;
    }

    /// <summary>
    /// Creates a ticket and browser binding.
    /// </summary>
    public (string Ticket, string Binding) Create(Guid userId, string providerId, string returnUrl)
    {
        Cleanup();

        var ticket = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var binding = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(_config.Current.TicketLifetimeSeconds, 30, 300));

        _tickets[TicketHash(ticket)] = new LoginTicket
        {
            TicketHash = TicketHash(ticket),
            BindingHash = TicketHash(binding),
            JellyfinUserId = userId,
            ProviderId = providerId,
            ReturnUrl = returnUrl,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime)
        };

        if (_tickets.Count > MaxEntries)
        {
            Cleanup(force: true);
        }

        return (ticket, binding);
    }

    /// <summary>
    /// Consumes a ticket. The ticket and binding are compared in constant time.
    /// </summary>
    public LoginTicket? Consume(string ticket, string? binding)
    {
        if (string.IsNullOrWhiteSpace(ticket)
            || string.IsNullOrWhiteSpace(binding))
        {
            return null;
        }

        var hash = TicketHash(ticket);
        if (!_tickets.TryGetValue(hash, out var record))
        {
            return null;
        }

        _tickets.TryRemove(hash, out _);

        if (record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        if (!FixedTimeEquals(record.BindingHash, TicketHash(binding)))
        {
            return null;
        }

        return record;
    }

    /// <summary>
    /// Gets the cookie name for the ticket binding.
    /// </summary>
    public static string CookieName => "jf-sso-ticket";

    private static string TicketHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private void Cleanup(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _tickets.TryRemove(pair.Key, out _);
            }
        }

        if (force && _tickets.Count > MaxEntries)
        {
            foreach (var pair in _tickets.OrderBy(x => x.Value.ExpiresAt).Take(_tickets.Count - MaxEntries))
            {
                _tickets.TryRemove(pair.Key, out _);
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
