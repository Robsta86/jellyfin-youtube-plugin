using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Selects the best Jellyfin-compatible (progressive, H.264/AAC, ≤1080p) format
/// from a yt-dlp JSON response.
///
/// Selection priority:
///   1. Progressive streams (video + audio in one file) – DASH-only videos are rejected.
///   2. Container: MP4.
///   3. Video codec: AVC / H.264 (vcodec starts with "avc").
///   4. Audio codec: AAC / mp4a (acodec starts with "mp4a").
///   5. Height ≤1080p.
///   6. Highest resolution first, then highest bitrate.
/// </summary>
public class FormatSelector
{
    private readonly ILogger<FormatSelector> _logger;

    /// <summary>Initializes a new instance of the <see cref="FormatSelector"/> class.</summary>
    public FormatSelector(ILogger<FormatSelector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the direct CDN URL for the best progressive format, or <c>null</c> when
    /// no compatible progressive format exists (i.e. only split DASH streams are available).
    /// </summary>
    public string? SelectBestFormat(JsonNode videoInfo)
    {
        var formats = videoInfo["formats"]?.AsArray();
        if (formats is null || formats.Count == 0)
        {
            _logger.LogWarning("yt-dlp response contains no 'formats' array.");
            return null;
        }

        var best = formats
            .Where(f => f is not null)
            .Where(IsProgressive)
            .Where(IsMp4)
            .Where(IsAvcAac)
            .Where(IsAtMost1080p)
            .OrderByDescending(f => GetInt(f, "height"))
            .ThenByDescending(f => GetDouble(f, "tbr"))
            .FirstOrDefault();

        if (best is null)
        {
            _logger.LogInformation(
                "No progressive AVC/AAC MP4 format ≤1080p found. "
                + "Only DASH or non-H.264 streams are available. "
                + "DASH proxy is not supported in v1.");
            return null;
        }

        var url = best["url"]?.GetValue<string>();
        _logger.LogDebug(
            "Selected format: id={FormatId} height={Height} tbr={Tbr}",
            GetString(best, "format_id"),
            GetInt(best, "height"),
            GetDouble(best, "tbr"));

        return url;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IsProgressive(JsonNode? f)
    {
        var vcodec = GetString(f, "vcodec");
        var acodec = GetString(f, "acodec");
        return vcodec != "none" && vcodec.Length > 0
            && acodec != "none" && acodec.Length > 0;
    }

    private static bool IsMp4(JsonNode? f)
        => GetString(f, "ext") == "mp4";

    private static bool IsAvcAac(JsonNode? f)
    {
        var vcodec = GetString(f, "vcodec");
        var acodec = GetString(f, "acodec");
        return vcodec.StartsWith("avc", System.StringComparison.OrdinalIgnoreCase)
            && acodec.StartsWith("mp4a", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtMost1080p(JsonNode? f)
        => GetInt(f, "height") <= 1080;

    private static string GetString(JsonNode? node, string key)
    {
        try
        {
            return node?[key]?.GetValue<string>() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int GetInt(JsonNode? node, string key)
    {
        try
        {
            return node?[key]?.GetValue<int>() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double GetDouble(JsonNode? node, string key)
    {
        try
        {
            return node?[key]?.GetValue<double>() ?? 0d;
        }
        catch
        {
            return 0d;
        }
    }
}
