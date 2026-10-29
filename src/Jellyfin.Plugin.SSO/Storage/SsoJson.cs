using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SSO.Storage;

/// <summary>
/// Shared JSON policy for plugin-owned data files.
/// </summary>
internal static class SsoJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
