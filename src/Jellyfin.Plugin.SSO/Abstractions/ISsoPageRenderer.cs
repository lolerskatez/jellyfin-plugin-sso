using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Renders the plugin's standalone HTML pages.
/// </summary>
public interface ISsoPageRenderer
{
    string Render(HttpRequest request, string fileName, IDictionary<string, string>? replacements = null);
}
