using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeSync;

/// <summary>
/// Wraps a live ffmpeg mux process and ensures it is torn down on cancellation or disposal.
/// </summary>
public sealed class FfmpegMuxSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderrTask;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="FfmpegMuxSession"/> class.</summary>
    public FfmpegMuxSession(Process process, ILogger logger)
    {
        _process = process;
        _logger = logger;
        _stderrTask = process.StandardError.ReadToEndAsync();
    }

    /// <summary>Copies ffmpeg stdout to the provided destination stream.</summary>
    public Task CopyOutputToAsync(Stream destination, CancellationToken cancellationToken)
        => _process.StandardOutput.BaseStream.CopyToAsync(destination, 81920, cancellationToken);

    /// <summary>
    /// Stops the backing ffmpeg process immediately when it is still running.
    /// Safe to call multiple times.
    /// </summary>
    public void Stop()
    {
        try
        {
            if (_disposed || _process.HasExited)
            {
                return;
            }

            _logger.LogDebug("Stopping ffmpeg merge process {ProcessId}", _process.Id);
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop ffmpeg merge process cleanly.");
        }
    }

    /// <summary>
    /// Waits for ffmpeg to exit and throws when the process fails.
    /// </summary>
    public async Task EnsureCompletedAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await _stderrTask.ConfigureAwait(false);

        if (_process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {_process.ExitCode}. {TrimError(stderr)}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogDebug("ffmpeg completed with stderr output: {Error}", TrimError(stderr));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Stop();

            if (!_process.HasExited)
            {
                await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop ffmpeg merge process cleanly.");
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static string TrimError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "No stderr output.";
        }

        var trimmed = stderr.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }
}