using System.Globalization;
using System.Text;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// Produces safe, Jellyfin-compatible usernames from IdP claims.
/// </summary>
public sealed class UsernameResolver : IUsernameResolver
{
    private const int MaxLength = 255;
    private readonly IUserManager _userManager;
    private readonly ILogger<UsernameResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsernameResolver"/> class.
    /// </summary>
    public UsernameResolver(IUserManager userManager, ILogger<UsernameResolver> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the preferred username for an identity.
    /// </summary>
    public string Resolve(OidcIdentity identity, OidcProviderConfig provider)
    {
        var candidates = new[]
        {
            identity.Username,
            GetEmailLocalPart(identity.Email),
            identity.DisplayName,
            "sso-" + AuditLog.Fingerprint(provider.Slug + "|" + identity.Issuer + "|" + identity.Subject)
        };

        foreach (var candidate in candidates)
        {
            var sanitized = Sanitize(candidate);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                return sanitized;
            }
        }

        return "sso-user";
    }

    /// <summary>
    /// Makes a username unique by appending a numeric suffix.
    /// </summary>
    public string MakeUnique(string username)
    {
        var candidate = Truncate(username, MaxLength);
        if (_userManager.GetUserByName(candidate) is null)
        {
            return candidate;
        }

        for (var i = 2; i < 1000; i++)
        {
            var suffix = "-" + i.ToString(CultureInfo.InvariantCulture);
            var value = Truncate(username, MaxLength - suffix.Length) + suffix;
            if (_userManager.GetUserByName(value) is null)
            {
                return value;
            }
        }

        return Truncate(username, 240) + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Finds an existing user by username.
    /// </summary>
    public Jellyfin.Database.Implementations.Entities.User? FindExisting(string username)
    {
        return _userManager.GetUserByName(username);
    }

    /// <summary>
    /// Sanitizes a username using Jellyfin's allowed character set.
    /// </summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormC).Trim();
        var builder = new StringBuilder(normalized.Length);
        var lastWasSpace = false;

        foreach (var character in normalized)
        {
            var allowed = char.IsLetterOrDigit(character)
                || character is '-' or '_' or '\'' or '.' or '@' or '+'
                || char.IsWhiteSpace(character);

            if (!allowed)
            {
                builder.Append('-');
                lastWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        var result = builder.ToString().Trim();
        if (result is "." or "..")
        {
            return string.Empty;
        }

        return Truncate(result, MaxLength);
    }

    private static string? GetEmailLocalPart(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    private static string Truncate(string value, int length)
    {
        return value.Length <= length ? value : value[..length].TrimEnd();
    }
}
