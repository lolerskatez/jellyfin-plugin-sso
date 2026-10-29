using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Shared helpers for the public SSO controllers.
/// </summary>
public abstract class SsoControllerBase : ControllerBase
{
    private readonly IPluginConfigAccessor _config;
    private readonly ISsoPageRenderer _pageRenderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SsoControllerBase"/> class.
    /// </summary>
    protected SsoControllerBase(IPluginConfigAccessor config, ISsoPageRenderer pageRenderer)
    {
        _config = config;
        _pageRenderer = pageRenderer;
    }

    /// <summary>
    /// Gets the live plugin configuration.
    /// </summary>
    protected PluginConfiguration Configuration => _config.Current;

    /// <summary>
    /// Finds an enabled provider by slug.
    /// </summary>
    protected OidcProviderConfig? FindProvider(string slug)
    {
        return Configuration.Providers.FirstOrDefault(x =>
            x.Enabled && string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the authenticated Jellyfin user ID from the current principal.
    /// </summary>
    protected Guid? GetCurrentUserId()
    {
        var value = User.FindFirst("Jellyfin-UserId")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// Gets the normalized remote endpoint.
    /// </summary>
    protected string GetRemoteIp()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Prefixes a local path with the current Jellyfin path base.
    /// </summary>
    protected string LocalUrl(string path)
    {
        return Request.PathBase.Add(new PathString(path)).Value ?? path;
    }

    /// <summary>
    /// Serves a plugin HTML page with no-store and a strict CSP.
    /// </summary>
    protected ActionResult HtmlPage(string fileName)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
        return Content(_pageRenderer.Render(Request, fileName), "text/html; charset=utf-8");
    }

    /// <summary>
    /// Redirects to the completion page with a safe error code.
    /// </summary>
    protected IActionResult RedirectToComplete(string code)
    {
        return Redirect(LocalUrl("/sso/complete?error=" + Uri.EscapeDataString(code)));
    }
}
