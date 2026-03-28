namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>Defines the type of a YouTube source (channel or playlist).</summary>
public enum SourceType
{
    /// <summary>A YouTube channel.</summary>
    Channel,

    /// <summary>A YouTube playlist.</summary>
    Playlist
}

/// <summary>Determines how videos from a playlist are organised inside Jellyfin.</summary>
public enum SourceMode
{
    /// <summary>Videos are treated as TV-show episodes (tvshow.nfo parent).</summary>
    Series,

    /// <summary>Videos are treated as individual movies.</summary>
    Movies
}

/// <summary>Describes a single YouTube source (channel or playlist) to sync.</summary>
public class SourceDefinition
{
    /// <summary>Gets or sets the YouTube channel ID or playlist ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the source type.</summary>
    public SourceType Type { get; set; } = SourceType.Channel;

    /// <summary>Gets or sets how playlist content is structured inside Jellyfin.</summary>
    public SourceMode Mode { get; set; } = SourceMode.Series;

    /// <summary>Gets or sets the human-readable display name used as the folder name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional description written into the source .nfo file.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets the yt-dlp-compatible URL for this source.</summary>
    public string Url => Type == SourceType.Channel
        ? $"https://www.youtube.com/channel/{Id}"
        : $"https://www.youtube.com/playlist?list={Id}";
}
