namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Represents the resolved playback strategy for a YouTube video.
/// </summary>
public sealed class PlaybackResolveResult
{
    private PlaybackResolveResult(string? redirectUrl, string? dashVideoUrl, string? dashAudioUrl)
    {
        RedirectUrl = redirectUrl;
        DashVideoUrl = dashVideoUrl;
        DashAudioUrl = dashAudioUrl;
    }

    /// <summary>Gets the direct progressive stream URL, when available.</summary>
    public string? RedirectUrl { get; }

    /// <summary>Gets the DASH video stream URL for live ffmpeg merging.</summary>
    public string? DashVideoUrl { get; }

    /// <summary>Gets the DASH audio stream URL for live ffmpeg merging.</summary>
    public string? DashAudioUrl { get; }

    /// <summary>Gets a value indicating whether this result is a direct redirect.</summary>
    public bool IsRedirect => !string.IsNullOrWhiteSpace(RedirectUrl);

    /// <summary>Gets a value indicating whether this result requires DASH live merge.</summary>
    public bool IsDashMerge => !string.IsNullOrWhiteSpace(DashVideoUrl) && !string.IsNullOrWhiteSpace(DashAudioUrl);

    /// <summary>Creates a redirect result.</summary>
    public static PlaybackResolveResult Redirect(string url) => new(url, null, null);

    /// <summary>Creates a DASH live-merge result.</summary>
    public static PlaybackResolveResult DashMerge(string videoUrl, string audioUrl) => new(null, videoUrl, audioUrl);
}