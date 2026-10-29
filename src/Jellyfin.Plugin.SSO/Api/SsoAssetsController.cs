using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Serves the plugin's embedded browser assets.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("sso/assets")]
public sealed class SsoAssetsController : ControllerBase
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".js"] = "application/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon"
    };

    /// <summary>
    /// Gets an embedded asset.
    /// </summary>
    [HttpGet("{name}")]
    [ResponseCache(NoStore = false, Location = ResponseCacheLocation.Any, Duration = 300)]
    public IActionResult Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal))
        {
            return NotFound();
        }

        var assembly = typeof(SsoAssetsController).Assembly;
        var resourceName = "Jellyfin.Plugin.SSO.Web.assets." + name;
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return NotFound();
        }

        var extension = Path.GetExtension(name);
        var contentType = ContentTypes.TryGetValue(extension, out var type)
            ? type
            : "application/octet-stream";

        Response.Headers[HeaderNames.CacheControl] = "public, max-age=300";
        return File(stream, contentType);
    }
}
