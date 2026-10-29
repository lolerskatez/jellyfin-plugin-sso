namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Limits repeated authentication requests.
/// </summary>
public interface ISsoRateLimiter
{
    bool IsAllowed(string key, int limit, TimeSpan window);
}
