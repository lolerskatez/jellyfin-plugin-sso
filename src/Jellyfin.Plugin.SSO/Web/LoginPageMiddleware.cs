using System.Text;
using System.Text.Encodings.Web;
using Jellyfin.Plugin.SSO.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Adds the SSO assets to the served Jellyfin web index without modifying web files.
/// </summary>
public sealed class LoginPageMiddleware
{
    private const string Marker = "<!-- jellyfin-sso-login -->";
    private readonly RequestDelegate _next;
    private readonly IApplicationPaths _applicationPaths;
    private readonly IPluginConfigAccessor _config;
    private readonly ILogger<LoginPageMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginPageMiddleware"/> class.
    /// </summary>
    public LoginPageMiddleware(
        RequestDelegate next,
        IApplicationPaths applicationPaths,
        IPluginConfigAccessor config,
        ILogger<LoginPageMiddleware> logger)
    {
        _next = next;
        _applicationPaths = applicationPaths;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_config.Current.LoginPage.InjectLoginPage
            || (context.Request.Method != HttpMethods.Get && context.Request.Method != HttpMethods.Head)
            || !IsWebIndexRequest(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var indexPath = Path.Combine(_applicationPaths.WebPath, "index.html");
        if (!File.Exists(indexPath))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        string html;
        try
        {
            html = await File.ReadAllTextAsync(indexPath, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read Jellyfin web index for SSO injection.");
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!html.Contains(Marker, StringComparison.Ordinal))
        {
            var assetBase = HtmlEncoder.Default.Encode(context.Request.PathBase.Add("/sso/assets").Value ?? "/sso/assets");
            var assetVersion = HtmlEncoder.Default.Encode(typeof(LoginPageMiddleware).Assembly.GetName().Version?.ToString() ?? "1");
            var css = $"    <link rel=\"stylesheet\" href=\"{assetBase}/login.css?v={assetVersion}\">\n";
            var script = $"    <script src=\"{assetBase}/login.js?v={assetVersion}\" defer></script>\n";

            if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</head>", css + "</head>", StringComparison.OrdinalIgnoreCase);
            }

            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", script + Marker + "\n</body>", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                html += script + Marker;
            }
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["X-SSO-Injected"] = "1";

        if (context.Request.Method == HttpMethods.Head)
        {
            return;
        }

        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static bool IsWebIndexRequest(PathString path)
    {
        var value = path.Value?.TrimEnd('/') ?? string.Empty;
        return value.Equals("/web", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase);
    }
}
