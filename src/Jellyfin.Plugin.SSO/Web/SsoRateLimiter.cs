using System.Collections.Concurrent;
using Jellyfin.Plugin.SSO.Abstractions;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Small in-memory sliding-window limiter for unauthenticated auth endpoints.
/// </summary>
public sealed class SsoRateLimiter : ISsoRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _buckets = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>
    /// Returns true when the key is under the configured limit.
    /// </summary>
    public bool IsAllowed(string key, int limit, TimeSpan window)
    {
        if (limit <= 0)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var cutoff = now - window;

        lock (_gate)
        {
            var bucket = _buckets.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
            while (bucket.Count > 0 && bucket.Peek() < cutoff)
            {
                bucket.Dequeue();
            }

            if (bucket.Count >= limit)
            {
                return false;
            }

            bucket.Enqueue(now);
            return true;
        }
    }
}
