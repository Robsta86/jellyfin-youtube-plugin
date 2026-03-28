using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Simple in-memory cache for resolved YouTube format selections.
/// Each entry expires after a configurable number of minutes.
/// </summary>
public class SimpleResolveCache
{
    private sealed record CacheEntry(SelectedFormat Format, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Tries to retrieve a cached <see cref="SelectedFormat"/> for the given video ID.
    /// Returns <c>false</c> (and sets <paramref name="format"/> to <c>null</c>) when the entry is absent or expired.
    /// </summary>
    public bool TryGet(string videoId, out SelectedFormat? format)
    {
        if (_cache.TryGetValue(videoId, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            format = entry.Format;
            return true;
        }

        format = null;
        return false;
    }

    /// <summary>Stores a resolved <see cref="SelectedFormat"/> in the cache with the given TTL in minutes.</summary>
    public void Set(string videoId, SelectedFormat format, int minutes)
    {
        _cache[videoId] = new CacheEntry(format, DateTime.UtcNow.AddMinutes(minutes));
    }
}
