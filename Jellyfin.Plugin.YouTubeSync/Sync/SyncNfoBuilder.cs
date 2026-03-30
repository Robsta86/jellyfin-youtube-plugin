using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.YouTubeSync.Configuration;
using Jellyfin.Plugin.YouTubeSync.Metadata;

namespace Jellyfin.Plugin.YouTubeSync.Sync;

internal static class SyncNfoBuilder
{
    public static string BuildTvShowNfo(
        SourceDefinition source,
        string name,
        string description,
        string thumbnailUrl,
        IReadOnlyList<PlaylistSeasonDefinition> playlistSeasonDefinitions)
    {
        var thumb = string.IsNullOrEmpty(thumbnailUrl)
            ? string.Empty
            : $"\n  <thumb aspect=\"poster\">{Xml(thumbnailUrl)}</thumb>";
        var namedSeasons = playlistSeasonDefinitions.Count == 0
            ? string.Empty
            : string.Concat(
                playlistSeasonDefinitions.Select(definition =>
                    $"\n  <namedseason number=\"{definition.SeasonNumber}\">{Xml(definition.Title)}</namedseason>"));

        return $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <tvshow>
          <title>{Xml(name)}</title>
          <plot>{Xml(description)}</plot>
          <uniqueid type="youtube" default="true">{Xml(source.Id)}</uniqueid>{thumb}{namedSeasons}
        </tvshow>
        """;
    }

    public static string BuildCollectionNfo(SourceDefinition source, string name, string description, string thumbnailUrl)
    {
        var thumb = string.IsNullOrEmpty(thumbnailUrl)
            ? string.Empty
            : $"\n  <thumb aspect=\"poster\">{Xml(thumbnailUrl)}</thumb>";

        return $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <movie>
          <title>{Xml(name)}</title>
          <plot>{Xml(description)}</plot>
          <uniqueid type="youtube" default="true">{Xml(source.Id)}</uniqueid>{thumb}
        </movie>
        """;
    }

    public static string BuildEpisodeNfo(VideoMetadata video, string sourceName, int? seasonNumber, int? episodeNumber)
    {
        var aired = BuildDateTag("aired", video.PublishedUtc);
        var premiered = BuildDateTag("premiered", video.PublishedUtc);
        var thumb = string.IsNullOrEmpty(video.ThumbnailUrl)
            ? string.Empty
            : $"\n  <thumb>{Xml(video.ThumbnailUrl)}</thumb>";
        var season = seasonNumber.HasValue ? $"\n  <season>{seasonNumber.Value}</season>" : string.Empty;
        var episode = episodeNumber.HasValue ? $"\n  <episode>{episodeNumber.Value}</episode>" : string.Empty;
        var runtime = video.DurationSeconds.HasValue ? $"\n  <runtime>{video.DurationSeconds.Value / 60}</runtime>" : string.Empty;
        var studio = string.IsNullOrWhiteSpace(video.ChannelName) ? string.Empty : $"\n  <studio>{Xml(video.ChannelName)}</studio>";

        return $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <episodedetails>
          <title>{Xml(video.Title)}</title>
          <showtitle>{Xml(sourceName)}</showtitle>
          <plot>{Xml(video.Description)}</plot>
                    <uniqueid type="youtube" default="true">{Xml(video.SyncId)}</uniqueid>{aired}{premiered}{season}{episode}{runtime}{studio}{thumb}
        </episodedetails>
        """;
    }

    public static string BuildMovieVideoNfo(VideoMetadata video, string sourceName)
    {
        var premiered = BuildDateTag("premiered", video.PublishedUtc);
        var thumb = string.IsNullOrEmpty(video.ThumbnailUrl)
            ? string.Empty
            : $"\n  <thumb>{Xml(video.ThumbnailUrl)}</thumb>";
        var runtime = video.DurationSeconds.HasValue ? $"\n  <runtime>{video.DurationSeconds.Value / 60}</runtime>" : string.Empty;
        var studio = string.IsNullOrWhiteSpace(video.ChannelName) ? string.Empty : $"\n  <studio>{Xml(video.ChannelName)}</studio>";
        var set = string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $"\n  <set>{Xml(sourceName)}</set>";

        return $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <movie>
          <title>{Xml(video.Title)}</title>
          <plot>{Xml(video.Description)}</plot>
                    <uniqueid type="youtube" default="true">{Xml(video.SyncId)}</uniqueid>{premiered}{runtime}{studio}{set}{thumb}
        </movie>
        """;
    }

    private static string BuildDateTag(string tagName, DateTime? date)
    {
        return date is DateTime value ? $"\n  <{tagName}>{value:yyyy-MM-dd}</{tagName}>" : string.Empty;
    }

    private static string Xml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
}