using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Helper for the plugin's short-lived auth cookies.
/// </summary>
public static class SsoCookieWriter
{
    /// <summary>
    /// Writes an HttpOnly cookie scoped to the SSO endpoints.
    /// </summary>
    public static void Write(
        HttpResponse response,
        string name,
        string value,
        int maxAgeSeconds,
        bool secure,
        SameSiteMode sameSite = SameSiteMode.Lax)
    {
        var pathBase = response.HttpContext.Request.PathBase.Value ?? string.Empty;
        response.Cookies.Append(
            name,
            value,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Path = pathBase + "/sso",
                MaxAge = TimeSpan.FromSeconds(maxAgeSeconds),
                IsEssential = true
            });
    }

    /// <summary>
    /// Deletes a cookie.
    /// </summary>
    public static void Delete(HttpResponse response, string name, bool secure)
    {
        var pathBase = response.HttpContext.Request.PathBase.Value ?? string.Empty;
        response.Cookies.Delete(
            name,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = pathBase + "/sso",
                IsEssential = true
            });
    }
}
