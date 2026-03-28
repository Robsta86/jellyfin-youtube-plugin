using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Simple in-memory cache for resolved YouTube playback results.
/// Each entry expires after a configurable number of minutes.
/// </summary>
public class SimpleResolveCache
{
    private sealed record CacheEntry(PlaybackResolveResult Result, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Tries to retrieve a cached resolve result for the given video ID.
    /// Returns <c>false</c> (and sets <paramref name="result"/> to <c>null</c>) when the entry is absent or expired.
    /// </summary>
    public bool TryGet(string videoId, out PlaybackResolveResult? result)
    {
        if (_cache.TryGetValue(videoId, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            result = entry.Result;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Stores a resolved playback result in the cache with the given TTL in minutes.</summary>
    public void Set(string videoId, PlaybackResolveResult result, int minutes)
    {
        _cache[videoId] = new CacheEntry(result, DateTime.UtcNow.AddMinutes(minutes));
    }
}

