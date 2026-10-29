using System.Text;
using Jellyfin.Plugin.SSO.Configuration;

namespace Jellyfin.Plugin.SSO.Oidc;

/// <summary>
/// Shared HTTP safety helpers for OIDC calls.
/// </summary>
internal static class OidcHttp
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;

    public static void EnsureAllowedScheme(string url, OidcProviderConfig config, bool allowHttp)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new OidcException("The OIDC endpoint is not a valid absolute URL.");
        }

        EnsureAllowedScheme(uri, config, allowHttp);
    }

    public static void EnsureAllowedScheme(Uri uri, OidcProviderConfig config, bool allowHttp)
    {
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (config.AllowHttp || allowHttp)
        {
            return;
        }

        throw new OidcException("OIDC endpoints must use HTTPS. Enable AllowHttp only for local development.");
    }

    public static async Task<string> ReadLimitedStringAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new OidcException("The identity provider response was too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (limited.Length + read > MaxResponseBytes)
            {
                throw new OidcException("The identity provider response was too large.");
            }

            limited.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(limited.ToArray());
    }

    public static string Truncate(string value, int length)
    {
        return value.Length <= length ? value : value[..length] + "...";
    }
}
