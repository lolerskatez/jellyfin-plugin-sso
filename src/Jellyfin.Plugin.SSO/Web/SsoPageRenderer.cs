using System.Text.Encodings.Web;
using Jellyfin.Plugin.SSO.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Renders the plugin's small standalone pages with a path-base-aware asset URL.
/// </summary>
public sealed class SsoPageRenderer : ISsoPageRenderer
{
    /// <summary>
    /// Renders an embedded page.
    /// </summary>
    public string Render(HttpRequest request, string fileName, IDictionary<string, string>? replacements = null)
    {
        var assembly = typeof(SsoPageRenderer).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.SSO.Web.assets." + fileName)
            ?? throw new InvalidOperationException("Missing embedded page " + fileName);
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();

        var pathBase = request.PathBase.Value ?? string.Empty;
        var assetBase = pathBase + "/sso/assets";
        var safePathBase = HtmlEncoder.Default.Encode(pathBase);
        var safeAssetBase = HtmlEncoder.Default.Encode(assetBase);

        html = html.Replace("{{PATH_BASE}}", safePathBase, StringComparison.Ordinal)
            .Replace("{{ASSET_BASE}}", safeAssetBase, StringComparison.Ordinal);

        if (replacements is not null)
        {
            foreach (var pair in replacements)
            {
                html = html.Replace(pair.Key, HtmlEncoder.Default.Encode(pair.Value), StringComparison.Ordinal);
            }
        }

        return html;
    }
}
