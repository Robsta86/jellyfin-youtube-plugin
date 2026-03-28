using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Describes a format (or pair of formats) selected for playback.
/// When <see cref="AudioFormatId"/> is set the two streams must be muxed on-the-fly;
/// when it is <c>null</c> <see cref="VideoUrl"/> is a self-contained progressive stream.
/// </summary>
public sealed record SelectedFormat(
    string VideoUrl,
    string VideoFormatId,
    int Height,
    string Ext,
    string? AudioUrl = null,
    string? AudioFormatId = null)
{
    /// <summary>
    /// <c>true</c> when video and audio are in separate DASH streams that must be muxed
    /// before playback; <c>false</c> for a self-contained progressive stream.
    /// </summary>
    public bool NeedsMuxing => AudioUrl is not null;
}

/// <summary>
/// Selects the best Jellyfin-compatible format (or pair of formats) from a yt-dlp JSON response.
///
/// Selection uses a tiered fallback strategy:
///   Tier 1: Progressive stream ≤1080p   – single combined file, highest resolution then MP4-preferred then bitrate.
///   Tier 2: Any progressive stream      – same ordering, no height cap.
///   Tier 3: DASH video ≤1080p + audio   – best H.264 (avc1) video paired with best AAC audio; needs muxing.
///   Tier 4: Any DASH video + audio      – last resort with no height cap.
///
/// DASH streams (Tiers 3/4) are streamed via a yt-dlp proxy that merges them on-the-fly using FFmpeg.
/// MP4/H.264 is preferred over other codecs at the same resolution.
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
    /// Returns a <see cref="SelectedFormat"/> describing the best stream(s) for playback,
    /// or <c>null</c> when no usable format is available.
    /// </summary>
    public SelectedFormat? SelectBestFormat(JsonNode videoInfo)
    {
        var formats = videoInfo["formats"]?.AsArray();
        if (formats is null || formats.Count == 0)
        {
            _logger.LogWarning("yt-dlp response contains no 'formats' array.");
            return null;
        }

        LogAvailableFormats(formats);

        // ── Tier 1/2: progressive (combined video+audio) ─────────────────────
        // Prefer ≤1080p first; fall back to any height when nothing fits.
        var progressive = PickBestProgressive(formats, maxHeight: 1080)
                       ?? PickBestProgressive(formats, maxHeight: null);

        if (progressive is not null)
        {
            var url = progressive["url"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(url))
            {
                _logger.LogInformation(
                    "Selected progressive format: id={FormatId} height={Height}p ext={Ext} tbr={Tbr} "
                    + "(no muxing required)",
                    GetString(progressive, "format_id"),
                    GetInt(progressive, "height"),
                    GetString(progressive, "ext"),
                    GetDouble(progressive, "tbr"));

                return new SelectedFormat(
                    VideoUrl: url,
                    VideoFormatId: GetString(progressive, "format_id"),
                    Height: GetInt(progressive, "height"),
                    Ext: GetString(progressive, "ext"));
            }
        }

        _logger.LogInformation(
            "No progressive stream found (YouTube only provides combined streams up to ~480p for most "
            + "modern/long videos). Falling back to DASH video+audio pair.");

        // ── Tier 3/4: DASH video + audio (needs muxing) ──────────────────────
        var (video, audio) = PickBestDash(formats, maxHeight: 1080);
        if (video is null || audio is null)
        {
            (video, audio) = PickBestDash(formats, maxHeight: null);
        }

        if (video is not null && audio is not null)
        {
            var videoUrl = video["url"]?.GetValue<string>();
            var audioUrl = audio["url"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(videoUrl) && !string.IsNullOrEmpty(audioUrl))
            {
                _logger.LogInformation(
                    "Selected DASH pair: video id={VId} height={Height}p ext={VExt} vcodec={VCodec} tbr={VTbr} | "
                    + "audio id={AId} ext={AExt} acodec={ACodec} abr={Abr} (will be muxed via yt-dlp+FFmpeg)",
                    GetString(video, "format_id"),
                    GetInt(video, "height"),
                    GetString(video, "ext"),
                    ShortenCodec(GetString(video, "vcodec")),
                    GetDouble(video, "tbr"),
                    GetString(audio, "format_id"),
                    GetString(audio, "ext"),
                    ShortenCodec(GetString(audio, "acodec")),
                    GetDouble(audio, "abr"));

                return new SelectedFormat(
                    VideoUrl: videoUrl,
                    VideoFormatId: GetString(video, "format_id"),
                    Height: GetInt(video, "height"),
                    Ext: GetString(video, "ext"),
                    AudioUrl: audioUrl,
                    AudioFormatId: GetString(audio, "format_id"));
            }
        }

        _logger.LogWarning(
            "No usable format found. Could not identify a compatible progressive or DASH stream pair.");
        return null;
    }

    // ── tier pickers ─────────────────────────────────────────────────────────

    private static JsonNode? PickBestProgressive(JsonArray formats, int? maxHeight)
    {
        var query = formats
            .Where(f => f is not null)
            .Where(IsProgressive);

        if (maxHeight.HasValue)
        {
            var limit = maxHeight.Value;
            query = query.Where(f => GetInt(f, "height") <= limit);
        }

        return query
            .OrderByDescending(f => GetInt(f, "height"))
            .ThenByDescending(f => IsMp4(f) ? 1 : 0)
            .ThenByDescending(f => GetDouble(f, "tbr"))
            .FirstOrDefault();
    }

    private static (JsonNode? video, JsonNode? audio) PickBestDash(JsonArray formats, int? maxHeight)
    {
        // Video-only DASH: has vcodec set, acodec explicitly "none"
        var videoQuery = formats
            .Where(f => f is not null)
            .Where(IsVideoOnlyDash);

        if (maxHeight.HasValue)
        {
            var limit = maxHeight.Value;
            // Exclude formats whose height is unknown (0) when applying a cap.
            videoQuery = videoQuery.Where(f => GetInt(f, "height") > 0 && GetInt(f, "height") <= limit);
        }

        // Prefer H.264 (avc1) for widest Jellyfin/FFmpeg compatibility,
        // then MP4 container, then highest total bitrate.
        var bestVideo = videoQuery
            .OrderByDescending(f => GetInt(f, "height"))
            .ThenByDescending(f => IsAvc1(f) ? 1 : 0)
            .ThenByDescending(f => IsMp4(f) ? 1 : 0)
            .ThenByDescending(f => GetDouble(f, "tbr"))
            .FirstOrDefault();

        if (bestVideo is null)
        {
            return (null, null);
        }

        // Audio-only DASH: vcodec "none", acodec set
        // Prefer AAC (mp4a) to match H.264 video; then highest audio bitrate.
        var bestAudio = formats
            .Where(f => f is not null)
            .Where(IsAudioOnlyDash)
            .OrderByDescending(f => IsM4a(f) ? 1 : 0)
            .ThenByDescending(f => GetDouble(f, "abr"))
            .FirstOrDefault();

        return (bestVideo, bestAudio);
    }

    // ── logging ──────────────────────────────────────────────────────────────

    private void LogAvailableFormats(JsonArray formats)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var progressive = new List<string>();
        var dashVideo = new List<string>();
        var dashAudio = new List<string>();
        var other = new List<string>();

        foreach (var f in formats.Where(f => f is not null))
        {
            var line = FormatSummary(f!);
            if (IsProgressive(f))
            {
                progressive.Add(line);
            }
            else if (IsVideoOnlyDash(f))
            {
                dashVideo.Add(line);
            }
            else if (IsAudioOnlyDash(f))
            {
                dashAudio.Add(line);
            }
            else
            {
                other.Add(line);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            $"Available formats ({formats.Count} total, "
            + $"{progressive.Count} progressive, "
            + $"{dashVideo.Count} DASH-video, "
            + $"{dashAudio.Count} DASH-audio, "
            + $"{other.Count} other):");

        if (progressive.Count > 0)
        {
            sb.AppendLine("  Progressive (combined video+audio):");
            progressive.ForEach(l => sb.AppendLine($"    {l}"));
        }

        if (dashVideo.Count > 0)
        {
            sb.AppendLine("  DASH video-only:");
            dashVideo.ForEach(l => sb.AppendLine($"    {l}"));
        }

        if (dashAudio.Count > 0)
        {
            sb.AppendLine("  DASH audio-only:");
            dashAudio.ForEach(l => sb.AppendLine($"    {l}"));
        }

        if (other.Count > 0)
        {
            sb.AppendLine("  Other (storyboard/thumbnail/etc.):");
            other.ForEach(l => sb.AppendLine($"    {l}"));
        }

        _logger.LogDebug("{FormatList}", sb.ToString().TrimEnd());
    }

    private static string FormatSummary(JsonNode f)
    {
        return $"[{GetString(f, "format_id")}] "
             + $"{GetInt(f, "height"),4}p "
             + $"{GetString(f, "ext"),-4} "
             + $"v={ShortenCodec(GetString(f, "vcodec")),-8} "
             + $"a={ShortenCodec(GetString(f, "acodec")),-8} "
             + $"tbr={GetDouble(f, "tbr"),7:F1}";
    }

    // ── stream type predicates ────────────────────────────────────────────────

    private static bool IsProgressive(JsonNode? f)
    {
        var vcodec = GetString(f, "vcodec");
        var acodec = GetString(f, "acodec");
        return vcodec != "none" && vcodec.Length > 0
            && acodec != "none" && acodec.Length > 0;
    }

    private static bool IsVideoOnlyDash(JsonNode? f)
    {
        var vcodec = GetString(f, "vcodec");
        var acodec = GetString(f, "acodec");
        return vcodec != "none" && vcodec.Length > 0 && acodec == "none";
    }

    private static bool IsAudioOnlyDash(JsonNode? f)
    {
        var vcodec = GetString(f, "vcodec");
        var acodec = GetString(f, "acodec");
        return vcodec == "none" && acodec != "none" && acodec.Length > 0;
    }

    // ── format property helpers ───────────────────────────────────────────────

    private static bool IsMp4(JsonNode? f)
        => GetString(f, "ext") == "mp4";

    private static bool IsM4a(JsonNode? f)
        => GetString(f, "ext") == "m4a";

    private static bool IsAvc1(JsonNode? f)
        => GetString(f, "vcodec").StartsWith("avc1", StringComparison.OrdinalIgnoreCase);

    private static string ShortenCodec(string codec)
    {
        if (codec.Length == 0)
        {
            return "(none)";
        }

        // e.g. "avc1.640028" → "avc1", "mp4a.40.2" → "mp4a"
        var dot = codec.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? codec : codec[..dot];
    }

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
        if (node?[key] is not { } valueNode)
        {
            return 0;
        }

        try
        {
            return valueNode.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            // Value may be stored as double in some yt-dlp versions (e.g. 720.0).
            try
            {
                return Convert.ToInt32(valueNode.GetValue<double>());
            }
            catch
            {
                return 0;
            }
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
