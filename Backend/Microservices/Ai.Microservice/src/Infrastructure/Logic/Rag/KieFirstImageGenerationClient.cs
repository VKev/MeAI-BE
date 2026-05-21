using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions.Kie;
using Application.Abstractions.Rag;
using Application.Kie.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Rag;

/// <summary>
/// Recommendation image generation prefers KIE GPT Image 2, then falls back to
/// OpenRouter's image-output model when KIE submit/poll/download fails.
/// </summary>
public sealed partial class KieFirstImageGenerationClient : IImageGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IKieImageService _kieImageService;
    private readonly OpenRouterImageGenerationClient _openRouterFallback;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KieFirstImageGenerationClient> _logger;
    private readonly string _textToImageModel;
    private readonly string _imageToImageModel;
    private readonly TimeSpan _pollTimeout;
    private readonly TimeSpan _pollInterval;

    public KieFirstImageGenerationClient(
        IKieImageService kieImageService,
        OpenRouterImageGenerationClient openRouterFallback,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<KieFirstImageGenerationClient> logger)
    {
        _kieImageService = kieImageService;
        _openRouterFallback = openRouterFallback;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _textToImageModel = configuration["Rag:KieImageGenTextModel"]
                            ?? configuration["RAG_KIE_IMAGE_GEN_TEXT_MODEL"]
                            ?? "gpt-image-2-text-to-image";
        _imageToImageModel = configuration["Rag:KieImageGenImageModel"]
                             ?? configuration["RAG_KIE_IMAGE_GEN_IMAGE_MODEL"]
                             ?? "gpt-image-2-image-to-image";
        _pollTimeout = TimeSpan.FromSeconds(ReadInt(
            configuration,
            "Rag:KieImageGenTimeoutSeconds",
            "RAG_KIE_IMAGE_GEN_TIMEOUT_SECONDS",
            300));
        _pollInterval = TimeSpan.FromSeconds(ReadInt(
            configuration,
            "Rag:KieImageGenPollIntervalSeconds",
            "RAG_KIE_IMAGE_GEN_POLL_INTERVAL_SECONDS",
            3));
    }

    public async Task<ImageGenerationResult> GenerateImageAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateWithKieAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "KIE GPT Image 2 generation failed; falling back to OpenRouter image generation.");
            return await _openRouterFallback.GenerateImageAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<ImageGenerationResult> GenerateWithKieAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var referenceUrls = request.ReferenceImageUrls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        var model = referenceUrls.Count > 0 ? _imageToImageModel : _textToImageModel;
        var aspectRatio = ExtractAspectRatio(request.Prompt) ?? "auto";
        var prompt = BuildPrompt(request);

        _logger.LogInformation(
            "KIE GPT Image 2 image-gen submit: model={Model} promptLen={PromptLen} refImages={RefCount} aspect={AspectRatio}",
            model,
            prompt.Length,
            referenceUrls.Count,
            aspectRatio);

        var submit = await _kieImageService.GenerateImageAsync(
            new KieGenerateRequest(
                Prompt: prompt,
                ImageInput: referenceUrls.Count > 0 ? referenceUrls : null,
                Model: model,
                AspectRatio: aspectRatio,
                Resolution: "1K",
                OutputFormat: "png",
                NumberOfVariances: 1,
                CorrelationId: null,
                UseCallback: false),
            cancellationToken).ConfigureAwait(false);

        if (!submit.Success || string.IsNullOrWhiteSpace(submit.TaskId))
        {
            throw new InvalidOperationException(
                $"KIE GPT Image 2 submit failed: {submit.Code} {submit.Message}");
        }

        var resultUrls = await PollResultUrlsAsync(submit.TaskId, cancellationToken)
            .ConfigureAwait(false);
        if (resultUrls.Count == 0)
        {
            throw new InvalidOperationException(
                $"KIE GPT Image 2 completed without result URLs. TaskId={submit.TaskId}");
        }

        var (dataUrl, mimeType) = await DownloadAsDataUrlAsync(
            resultUrls[0],
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "KIE GPT Image 2 image-gen completed: taskId={TaskId} mime={MimeType} dataUrlLen={Length}",
            submit.TaskId,
            mimeType,
            dataUrl.Length);

        return new ImageGenerationResult(
            DataUrl: dataUrl,
            MimeType: mimeType,
            PromptTokens: null,
            CompletionTokens: null,
            CostUsd: null);
    }

    private async Task<IReadOnlyList<string>> PollResultUrlsAsync(
        string taskId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _pollTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var details = await _kieImageService.GetImageDetailsAsync(taskId, cancellationToken)
                .ConfigureAwait(false);
            if (!details.Success)
            {
                _logger.LogWarning(
                    "KIE GPT Image 2 recordInfo failed while polling. taskId={TaskId} code={Code} message={Message}",
                    taskId,
                    details.Code,
                    details.Message);
            }
            else
            {
                var state = details.Data?.State ?? string.Empty;
                if (IsSuccessState(state))
                {
                    return ExtractResultUrls(details.Data?.ResultJson);
                }

                if (IsFailedState(state))
                {
                    throw new InvalidOperationException(
                        $"KIE GPT Image 2 task failed. taskId={taskId} state={state} " +
                        $"failCode={details.Data?.FailCode} failMsg={details.Data?.FailMsg}");
                }
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < _pollInterval ? remaining : _pollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"KIE GPT Image 2 task did not complete within {_pollTimeout.TotalSeconds:N0}s. TaskId={taskId}");
    }

    private async Task<(string DataUrl, string MimeType)> DownloadAsDataUrlAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (imageUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return (imageUrl, ExtractMimeTypeFromDataUrl(imageUrl) ?? "image/png");
        }

        var http = _httpClientFactory.CreateClient();
        using var response = await http.GetAsync(imageUrl, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"KIE result image download failed: HTTP {(int)response.StatusCode}");
        }

        var mimeType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mimeType) ||
            !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            mimeType = "image/png";
        }

        return ($"data:{mimeType};base64,{Convert.ToBase64String(bytes)}", mimeType);
    }

    private static IReadOnlyList<string> ExtractResultUrls(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return Array.Empty<string>();
        }

        var parsed = JsonSerializer.Deserialize<KieResultJson>(resultJson, JsonOptions);
        if (parsed?.ResultUrls is { Count: > 0 })
        {
            return parsed.ResultUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToList();
        }

        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        var urls = new List<string>();
        AddUrlArray(root, "resultUrls", urls);
        AddUrlArray(root, "result_urls", urls);
        AddUrlString(root, "resultImageUrl", urls);
        AddUrlString(root, "resultUrl", urls);
        AddUrlString(root, "url", urls);
        return urls;
    }

    private static void AddUrlArray(JsonElement root, string propertyName, List<string> urls)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var url = item.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    urls.Add(url);
                }
            }
        }
    }

    private static void AddUrlString(JsonElement root, string propertyName, List<string> urls)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            var url = value.GetString();
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url);
            }
        }
    }

    private static string BuildPrompt(ImageGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return request.Prompt;
        }

        return $"{request.SystemPrompt.Trim()}\n\nImage generation task:\n{request.Prompt}";
    }

    private static string? ExtractAspectRatio(string prompt)
    {
        var match = AspectRatioRegex().Match(prompt);
        return match.Success ? match.Groups["ratio"].Value : null;
    }

    private static string? ExtractMimeTypeFromDataUrl(string dataUrl)
    {
        var separator = dataUrl.IndexOf(';');
        return separator > "data:".Length ? dataUrl["data:".Length..separator] : null;
    }

    private static bool IsSuccessState(string state)
        => state.Trim().Equals("success", StringComparison.OrdinalIgnoreCase) ||
           state.Trim().Equals("completed", StringComparison.OrdinalIgnoreCase) ||
           state.Trim().Equals("complete", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedState(string state)
        => state.Trim().Equals("fail", StringComparison.OrdinalIgnoreCase) ||
           state.Trim().Equals("failed", StringComparison.OrdinalIgnoreCase) ||
           state.Trim().Equals("error", StringComparison.OrdinalIgnoreCase) ||
           state.Trim().Equals("canceled", StringComparison.OrdinalIgnoreCase) ||
           state.Trim().Equals("cancelled", StringComparison.OrdinalIgnoreCase);

    private static int ReadInt(
        IConfiguration configuration,
        string key,
        string envKey,
        int fallback)
    {
        return int.TryParse(configuration[key] ?? configuration[envKey], out var value)
            ? value
            : fallback;
    }

    [GeneratedRegex(@"Aspect\s+ratio:\s*(?<ratio>[0-9]+:[0-9]+|auto)", RegexOptions.IgnoreCase)]
    private static partial Regex AspectRatioRegex();
}
