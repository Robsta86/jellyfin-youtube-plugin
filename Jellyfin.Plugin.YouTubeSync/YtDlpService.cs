using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Wraps yt-dlp invocations. All methods run yt-dlp out-of-process and parse its JSON output.
/// </summary>
public class YtDlpService
{
    private readonly ILogger<YtDlpService> _logger;

    /// <summary>Initializes a new instance of the <see cref="YtDlpService"/> class.</summary>
    public YtDlpService(ILogger<YtDlpService> logger)
    {
        _logger = logger;
    }

    private string YtDlpPath => Plugin.Instance?.Configuration.YtDlpPath ?? "yt-dlp";

    /// <summary>
    /// Returns full yt-dlp JSON metadata for a single video (equivalent to <c>yt-dlp -J</c>).
    /// </summary>
    public Task<JsonNode?> GetVideoInfoAsync(string videoId, CancellationToken cancellationToken)
    {
        var url = $"https://www.youtube.com/watch?v={videoId}";
        return RunYtDlpJsonAsync(new[] { "-J", "--no-playlist", url }, cancellationToken);
    }

    /// <summary>
    /// Returns the flat-playlist JSON entry list for a channel or playlist URL.
    /// Each entry contains at minimum <c>id</c>, <c>title</c>, and <c>url</c>.
    /// </summary>
    public async Task<IReadOnlyList<JsonNode>> GetPlaylistEntriesAsync(
        string playlistUrl,
        int maxVideos,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { "--flat-playlist", "-J" };
        if (maxVideos > 0)
        {
            args.Add("--playlist-end");
            args.Add(maxVideos.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        args.Add(playlistUrl);

        var result = await RunYtDlpJsonAsync(args, cancellationToken).ConfigureAwait(false);
        var entries = result?["entries"]?.AsArray();
        if (entries is null)
        {
            return Array.Empty<JsonNode>();
        }

        var list = new List<JsonNode>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is not null)
            {
                list.Add(entry);
            }
        }

        return list;
    }

    private async Task<JsonNode?> RunYtDlpJsonAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        _logger.LogDebug("Running yt-dlp with arguments: {Arguments}", string.Join(" ", psi.ArgumentList));

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "yt-dlp exited with code {ExitCode}. Stderr: {Error}",
                    process.ExitCode,
                    error);
                return null;
            }

            return JsonNode.Parse(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run yt-dlp");
            return null;
        }
    }
}
