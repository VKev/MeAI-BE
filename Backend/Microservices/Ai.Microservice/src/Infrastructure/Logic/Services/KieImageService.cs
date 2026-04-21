using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Kie;
using Infrastructure.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.Services;

public sealed class KieImageService : IKieImageService
{
    private readonly HttpClient _httpClient;
    private readonly VeoOptions _options;
    private readonly ILogger<KieImageService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public KieImageService(
        HttpClient httpClient,
        IOptions<VeoOptions> options,
        ILogger<KieImageService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<KieGenerateResult> GenerateImageAsync(
        KieGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Kie API key is not configured");
            return new KieGenerateResult(false, 401, "Kie API key is not configured", null);
        }

        var model = request.Model ?? "nano-banana-pro";

        // Flux Kontext uses a dedicated endpoint with a different request shape.
        var isFluxKontext = model.StartsWith("flux-kontext-", StringComparison.OrdinalIgnoreCase);
        var endpoint = isFluxKontext ? "/api/v1/flux/kontext/generate" : "/api/v1/jobs/createTask";
        object payload = isFluxKontext
            ? BuildFluxKontextPayload(model, request)
            : new KieCreateTaskRequest
            {
                Model = model,
                Input = BuildInputParams(model, request),
                CallBackUrl = BuildCallbackUrl(request.CorrelationId)
            };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
            httpRequest.Content = JsonContent.Create(payload, payload.GetType(), options: JsonOptions);

            _logger.LogInformation("Sending image generation request to Kie API. Model: {Model}, Endpoint: {Endpoint}, AspectRatio: {AspectRatio}, Resolution: {Resolution}",
                model, endpoint, request.AspectRatio, request.Resolution);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogDebug("Kie API response: {StatusCode} - {Content}", response.StatusCode, content);

            var apiResponse = JsonSerializer.Deserialize<KieApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                _logger.LogError("Failed to deserialize Kie API response");
                return new KieGenerateResult(false, 500, "Failed to parse API response", null);
            }

            if (apiResponse.Code == 200 && apiResponse.Data?.TaskId is not null)
            {
                _logger.LogInformation("Image generation task created: {TaskId}", apiResponse.Data.TaskId);
                return new KieGenerateResult(true, 200, apiResponse.Msg ?? "Success", apiResponse.Data.TaskId);
            }

            _logger.LogWarning("Kie API returned error: {Code} - {Message}", apiResponse.Code, apiResponse.Msg);
            return new KieGenerateResult(false, apiResponse.Code, apiResponse.Msg ?? "Unknown error", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while calling Kie API");
            return new KieGenerateResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calling Kie API");
            return new KieGenerateResult(false, 500, $"Unexpected error: {ex.Message}", null);
        }
    }

    public async Task<KieRecordInfoResult> GetImageDetailsAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Kie API key is not configured");
            return new KieRecordInfoResult(false, 401, "Kie API key is not configured", null);
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/jobs/recordInfo?taskId={Uri.EscapeDataString(taskId)}");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

            _logger.LogInformation("Querying image details for task {TaskId}", taskId);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogDebug("Kie API response: {StatusCode} - {Content}", response.StatusCode, content);

            var apiResponse = JsonSerializer.Deserialize<KieRecordInfoApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                _logger.LogError("Failed to deserialize Kie API response");
                return new KieRecordInfoResult(false, 500, "Failed to parse API response", null);
            }

            if (apiResponse.Code == 200 && apiResponse.Data is not null)
            {
                var data = apiResponse.Data;
                var recordInfo = new KieRecordInfo(
                    TaskId: data.TaskId ?? taskId,
                    Model: data.Model,
                    State: data.State ?? "waiting",
                    ParamJson: data.Param,
                    ResultJson: data.ResultJson,
                    FailCode: data.FailCode,
                    FailMsg: data.FailMsg,
                    CostTime: data.CostTime,
                    CompleteTime: data.CompleteTime,
                    CreateTime: data.CreateTime);

                _logger.LogInformation("Image details retrieved for task {TaskId}, state: {State}", taskId, data.State);
                return new KieRecordInfoResult(true, 200, apiResponse.Msg ?? "Success", recordInfo);
            }

            _logger.LogWarning("Kie API returned error: {Code} - {Message}", apiResponse.Code, apiResponse.Msg);
            return new KieRecordInfoResult(false, apiResponse.Code, apiResponse.Msg ?? "Unknown error", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while calling Kie API");
            return new KieRecordInfoResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calling Kie API");
            return new KieRecordInfoResult(false, 500, $"Unexpected error: {ex.Message}", null);
        }
    }

    private string? BuildCallbackUrl(Guid? correlationId)
    {
        if (string.IsNullOrWhiteSpace(_options.CallbackUrl))
        {
            return null;
        }

        var callbackBaseUrl = NormalizeCallbackBaseUrl(_options.CallbackUrl);

        if (correlationId.HasValue)
        {
            return $"{callbackBaseUrl}/{correlationId.Value:D}?provider=image";
        }

        return $"{callbackBaseUrl}?provider=image";
    }

    private static string NormalizeCallbackBaseUrl(string callbackUrl)
    {
        var normalized = callbackUrl.TrimEnd('/');

        normalized = normalized.Replace(
            "/api/Ai/veo/callback",
            "/api/Ai/kie/callback",
            StringComparison.OrdinalIgnoreCase);

        normalized = normalized.Replace(
            "/api/Ai/image/callback",
            "/api/Ai/kie/callback",
            StringComparison.OrdinalIgnoreCase);

        if (!normalized.Contains("/api/Ai/kie/callback", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{normalized}/api/Ai/kie/callback";
        }

        return normalized;
    }

    private KieFluxKontextRequest BuildFluxKontextPayload(string model, KieGenerateRequest request)
    {
        // Flux Kontext editing mode: prompt + inputImage + aspectRatio.
        // Model will crop/pad the input to match aspectRatio when supplied.
        var inputImage = request.ImageInput is { Count: > 0 } ? request.ImageInput[0] : null;
        var prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? "Keep the exact same subject and composition; only adjust the framing to match the new aspect ratio."
            : request.Prompt;

        return new KieFluxKontextRequest
        {
            Prompt = prompt,
            InputImage = inputImage,
            AspectRatio = NormalizeAspectRatioForFlux(request.AspectRatio),
            Model = model,
            OutputFormat = "png",
            EnableTranslation = false,
            PromptUpsampling = false,
            SafetyTolerance = 2,
            CallBackUrl = BuildCallbackUrl(request.CorrelationId)
        };
    }

    private static string NormalizeAspectRatioForFlux(string aspectRatio)
    {
        // Flux Kontext supports: 21:9, 16:9, 4:3, 1:1, 3:4, 9:16
        return aspectRatio switch
        {
            "3:2" or "4:3" or "5:4" => "4:3",
            "2:3" or "3:4" or "4:5" => "3:4",
            "21:9" or "16:9" => aspectRatio,
            "9:16" or "1:1" => aspectRatio,
            _ => "1:1"
        };
    }

    private static Dictionary<string, object?> BuildInputParams(string model, KieGenerateRequest request)
    {
        // Ideogram V3 Reframe: resizes an existing image into a different aspect ratio.
        // Needs image_url (single string) + image_size — no prompt, no image_input.
        // Explicit rendering_speed=TURBO for fast reframe turnaround.
        if (string.Equals(model, "ideogram/v3-reframe", StringComparison.OrdinalIgnoreCase))
        {
            var sourceUrl = request.ImageInput is { Count: > 0 } ? request.ImageInput[0] : null;
            return new Dictionary<string, object?>
            {
                ["image_url"] = sourceUrl,
                ["image_size"] = MapAspectRatioToIdeogramSize(request.AspectRatio),
                ["rendering_speed"] = "TURBO",
                ["num_images"] = "1",
                ["style"] = "AUTO"
            };
        }

        var input = new Dictionary<string, object?> { ["prompt"] = request.Prompt };

        if (request.ImageInput is { Count: > 0 })
        {
            input["image_input"] = request.ImageInput;
        }

        // Ideogram uses image_size with different value format (e.g. square, landscape_16_9)
        if (model.StartsWith("ideogram/", StringComparison.OrdinalIgnoreCase))
        {
            input["image_size"] = MapAspectRatioToIdeogramSize(request.AspectRatio);
        }
        else if (!string.IsNullOrWhiteSpace(request.AspectRatio))
        {
            input["aspect_ratio"] = NormalizeAspectRatioForModel(model, request.AspectRatio);
        }

        // nano-banana-pro specific params
        if (model is "nano-banana-pro")
        {
            input["resolution"] = request.Resolution;
            input["output_format"] = request.OutputFormat;
            input["number_of_variances"] = request.NumberOfVariances;
        }

        // flux-2 supports resolution
        if (model.StartsWith("flux-2/", StringComparison.OrdinalIgnoreCase))
        {
            input["resolution"] = request.Resolution;
        }

        return input;
    }

    private static string NormalizeAspectRatioForModel(string model, string aspectRatio)
    {
        // Flux 2 supports: 1:1, 4:3, 3:4, 16:9, 9:16, 3:2, 2:3
        if (model.StartsWith("flux-2/", StringComparison.OrdinalIgnoreCase))
        {
            return aspectRatio switch
            {
                "5:4" or "21:9" => "16:9",
                "4:5" => "9:16",
                _ => aspectRatio
            };
        }

        // Grok Imagine supports: 2:3, 3:2, 1:1, 16:9, 9:16
        if (model.StartsWith("grok-imagine/", StringComparison.OrdinalIgnoreCase))
        {
            return aspectRatio switch
            {
                "4:3" or "5:4" or "21:9" => "3:2",
                "3:4" or "4:5" => "2:3",
                _ => aspectRatio
            };
        }

        // nano-banana-pro accepts all FE ratios as-is
        return aspectRatio;
    }

    private static string MapAspectRatioToIdeogramSize(string? aspectRatio)
    {
        // Ideogram only supports: square, square_hd, portrait_4_3, portrait_16_9, landscape_4_3, landscape_16_9
        // Map FE ratios to the closest supported value
        return aspectRatio switch
        {
            "1:1" => "square_hd",
            "16:9" => "landscape_16_9",
            "9:16" => "portrait_16_9",
            "4:3" => "landscape_4_3",
            "3:4" => "portrait_4_3",
            "3:2" => "landscape_4_3",   // closest landscape
            "2:3" => "portrait_4_3",    // closest portrait
            "5:4" => "landscape_4_3",   // closest landscape
            "4:5" => "portrait_4_3",    // closest portrait
            "21:9" => "landscape_16_9", // closest wide landscape
            _ => "square_hd"
        };
    }

    #region Private API Models

    private sealed class KieCreateTaskRequest
    {
        public string Model { get; set; } = "nano-banana-pro";
        public Dictionary<string, object?> Input { get; set; } = new();
        public string? CallBackUrl { get; set; }
    }

    private sealed class KieFluxKontextRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string? InputImage { get; set; }
        public string? AspectRatio { get; set; }
        public string? Model { get; set; }
        public string? OutputFormat { get; set; }
        public bool EnableTranslation { get; set; }
        public bool PromptUpsampling { get; set; }
        public int SafetyTolerance { get; set; }
        public string? CallBackUrl { get; set; }
    }

    private sealed class KieApiResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public KieApiResponseData? Data { get; set; }
    }

    private sealed class KieApiResponseData
    {
        public string? TaskId { get; set; }
    }

    private sealed class KieRecordInfoApiResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public KieRecordInfoData? Data { get; set; }
    }

    private sealed class KieRecordInfoData
    {
        public string? TaskId { get; set; }
        public string? Model { get; set; }
        public string? State { get; set; }
        public string? Param { get; set; }
        public string? ResultJson { get; set; }
        public string? FailCode { get; set; }
        public string? FailMsg { get; set; }
        public long? CostTime { get; set; }
        public long? CompleteTime { get; set; }
        public long CreateTime { get; set; }
    }

    #endregion
}
