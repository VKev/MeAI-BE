using System.Diagnostics;
using Application.Abstractions.Publishing;
using Application.Abstractions.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Publishing;

public sealed class FfmpegSocialPublishVideoTranscoder : ISocialPublishVideoTranscoder
{
    private const long MaxSourceVideoBytes = 512L * 1024 * 1024;
    private const long MaxMp4VideoBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan TranscodeTimeout = TimeSpan.FromMinutes(10);
    private readonly string _ffmpegPath;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FfmpegSocialPublishVideoTranscoder> _logger;

    public FfmpegSocialPublishVideoTranscoder(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FfmpegSocialPublishVideoTranscoder> logger)
    {
        _httpClient = httpClientFactory.CreateClient("SocialPublishMedia");
        _ffmpegPath = configuration["SocialPublishMedia:FfmpegPath"]?.Trim() ?? "ffmpeg";
        _logger = logger;
    }

    public async Task<Result<byte[]>> ConvertToMp4Async(
        UserResourcePresignResult resource,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meai-social-publish-{Guid.NewGuid():N}");
        var inputPath = Path.Combine(tempDirectory, "source-video");
        var outputPath = Path.Combine(tempDirectory, "normalized-video.mp4");

        try
        {
            Directory.CreateDirectory(tempDirectory);

            var downloadResult = await DownloadSourceAsync(resource, inputPath, cancellationToken);
            if (downloadResult.IsFailure)
            {
                return Result.Failure<byte[]>(downloadResult.Error);
            }

            var transcodeResult = await RunFfmpegAsync(inputPath, outputPath, cancellationToken);
            if (transcodeResult.IsFailure)
            {
                return Result.Failure<byte[]>(transcodeResult.Error);
            }

            var outputInfo = new FileInfo(outputPath);
            if (!outputInfo.Exists || outputInfo.Length == 0)
            {
                return Result.Failure<byte[]>(
                    new Error("SocialPublish.VideoConversionFailed", "Converted social publish video is empty."));
            }

            if (outputInfo.Length > MaxMp4VideoBytes)
            {
                return Result.Failure<byte[]>(
                    new Error(
                        "SocialPublish.VideoConversionTooLarge",
                        "Converted social publish video exceeds the 256 MB upload limit."));
            }

            return Result.Success(await File.ReadAllBytesAsync(outputPath, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Social publish video conversion failed. ResourceId: {ResourceId}",
                resource.ResourceId);
            return Result.Failure<byte[]>(
                new Error(
                    "SocialPublish.VideoConversionFailed",
                    $"Could not convert video to a supported MP4 format: {ex.Message}"));
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task<Result<bool>> DownloadSourceAsync(
        UserResourcePresignResult resource,
        string inputPath,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            resource.PresignedUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<bool>(
                new Error(
                    "SocialPublish.VideoConversionFetchFailed",
                    $"Could not fetch video for social publish conversion. Status {(int)response.StatusCode}."));
        }

        if (response.Content.Headers.ContentLength > MaxSourceVideoBytes)
        {
            return Result.Failure<bool>(
                new Error(
                    "SocialPublish.VideoConversionTooLarge",
                    "Video is too large to convert for social publishing."));
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(inputPath);
        var copyResult = await CopyBoundedAsync(
            source,
            destination,
            MaxSourceVideoBytes,
            cancellationToken);

        return copyResult;
    }

    private async Task<Result<bool>> RunFfmpegAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TranscodeTimeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in new[]
                 {
                     "-hide_banner",
                     "-loglevel", "error",
                     "-y",
                     "-i", inputPath,
                     "-map", "0:v:0",
                     "-map", "0:a?",
                     "-c:v", "libx264",
                     "-preset", "veryfast",
                     "-crf", "23",
                     "-pix_fmt", "yuv420p",
                     "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                     "-r", "30",
                     "-c:a", "aac",
                     "-b:a", "128k",
                     "-movflags", "+faststart",
                     "-f", "mp4",
                     outputPath
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return Result.Failure<bool>(
                    new Error("SocialPublish.VideoConversionFailed", "Could not start FFmpeg."));
            }

            var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            var errorOutput = await errorTask;

            if (process.ExitCode == 0)
            {
                return Result.Success(true);
            }

            return Result.Failure<bool>(
                new Error(
                    "SocialPublish.VideoConversionFailed",
                    $"FFmpeg could not normalize the video. {Truncate(errorOutput, 800)}"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return Result.Failure<bool>(
                new Error("SocialPublish.VideoConversionTimeout", "Video conversion timed out."));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<Result<bool>> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return Result.Success(true);
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                return Result.Failure<bool>(
                    new Error(
                        "SocialPublish.VideoConversionTooLarge",
                        "Video is too large to convert for social publishing."));
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after cancellation or timeout.
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup of temporary conversion files.
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
