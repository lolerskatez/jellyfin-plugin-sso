using System.Text.Json;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Reads values from a JSON claim payload using dotted paths.
/// </summary>
public sealed class ClaimSet
{
    private readonly JsonElement _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimSet"/> class.
    /// </summary>
    public ClaimSet(JsonElement root)
    {
        _root = root.Clone();
    }

    /// <summary>
    /// Gets the raw root element.
    /// </summary>
    public JsonElement Root => _root;

    /// <summary>
    /// Gets a string claim.
    /// </summary>
    public string? GetString(string path)
    {
        var element = GetElement(path);
        return element switch
        {
            { ValueKind: JsonValueKind.String } => element.Value.GetString(),
            { ValueKind: JsonValueKind.Number } => element.Value.GetRawText(),
            { ValueKind: JsonValueKind.True } => "true",
            { ValueKind: JsonValueKind.False } => "false",
            { ValueKind: JsonValueKind.Array } => element.Value.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            _ => null
        };
    }

    /// <summary>
    /// Gets a boolean claim.
    /// </summary>
    public bool GetBool(string path)
    {
        var element = GetElement(path);
        return element switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } => bool.TryParse(element.Value.GetString(), out var value) && value,
            _ => false
        };
    }

    /// <summary>
    /// Gets a list of string claims. Arrays and space/comma separated strings are supported.
    /// </summary>
    public IReadOnlyList<string> GetStrings(string path)
    {
        var element = GetElement(path);
        if (element is null)
        {
            return [];
        }

        var result = new List<string>();
        AddStrings(element.Value, result);
        return result
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets a raw claim element.
    /// </summary>
    public JsonElement? GetElement(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var current = _root;
        foreach (var part in SplitPath(path))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static void AddStrings(JsonElement element, List<string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                // Some providers serialize an array as a JSON string.
                if (value.TrimStart().StartsWith('['))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(value);
                        AddStrings(document.RootElement, result);
                        return;
                    }
                    catch (JsonException)
                    {
                        // Treat it as a normal string.
                    }
                }

                // Treat a string-valued group claim as one exact role/group name.
                // Authentik group names may contain spaces. Providers that return
                // multiple groups should use a JSON array.
                result.Add(value.Trim());

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddStrings(item, result);
                }

                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result.Add(element.GetRawText());
                break;
        }
    }

    private static IEnumerable<string> SplitPath(string path)
    {
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < path.Length; i++)
        {
            var character = path[i];
            if (character == '\\' && i + 1 < path.Length && path[i + 1] == '.')
            {
                current.Append('.');
                i++;
                continue;
            }

            if (character == '.')
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}

/// <summary>
/// Extracts the normalized identity from an ID token and optional UserInfo response.
/// </summary>
public sealed class ClaimExtractor
{
    /// <summary>
    /// Extracts an identity.
    /// </summary>
    public OidcIdentity Extract(
        ClaimSet tokenClaims,
        ClaimSet? userInfoClaims,
        Configuration.OidcProviderConfig provider)
    {
        var issuer = tokenClaims.GetString("iss") ?? string.Empty;
        var subject = tokenClaims.GetString("sub") ?? string.Empty;

        var username = GetFirstNonEmpty(
            tokenClaims.GetString(provider.UsernameClaim),
            userInfoClaims?.GetString(provider.UsernameClaim),
            tokenClaims.GetString("preferred_username"),
            userInfoClaims?.GetString("preferred_username"),
            tokenClaims.GetString("email"),
            userInfoClaims?.GetString("email"));

        var email = GetFirstNonEmpty(
            tokenClaims.GetString(provider.EmailClaim),
            userInfoClaims?.GetString(provider.EmailClaim));

        var displayName = GetFirstNonEmpty(
            tokenClaims.GetString(provider.DisplayNameClaim),
            userInfoClaims?.GetString(provider.DisplayNameClaim),
            username);

        var groups = new List<string>();
        foreach (var path in provider.GroupClaimPaths)
        {
            groups.AddRange(tokenClaims.GetStrings(path));
            if (userInfoClaims is not null)
            {
                groups.AddRange(userInfoClaims.GetStrings(path));
            }
        }

        var verified = tokenClaims.GetBool("email_verified")
            || (userInfoClaims?.GetBool("email_verified") ?? false);

        return new OidcIdentity
        {
            Issuer = issuer,
            Subject = subject,
            Username = username ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            Email = email ?? string.Empty,
            EmailVerified = verified,
            Groups = groups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string? GetFirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }
}

/// <summary>
/// Normalized external identity.
/// </summary>
public sealed class OidcIdentity
{
    /// <summary>
    /// Gets or sets the issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the suggested username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the email is verified.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Gets or sets the external groups.
    /// </summary>
    public IReadOnlyList<string> Groups { get; set; } = [];
}
