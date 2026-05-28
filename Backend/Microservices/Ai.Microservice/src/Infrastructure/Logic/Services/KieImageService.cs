using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.ApiCredentials;
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
    private readonly IApiCredentialProvider _credentialProvider;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public KieImageService(
        HttpClient httpClient,
        IOptions<VeoOptions> options,
        IApiCredentialProvider credentialProvider,
        ILogger<KieImageService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _credentialProvider = credentialProvider;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<KieGenerateResult> GenerateImageAsync(
        KieGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialProvider.GetOptionalValue("Kie", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
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
                CallBackUrl = request.UseCallback ? BuildCallbackUrl(request.CorrelationId) : null
            };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
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
        var apiKey = _credentialProvider.GetOptionalValue("Kie", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("Kie API key is not configured");
            return new KieRecordInfoResult(false, 401, "Kie API key is not configured", null);
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/jobs/recordInfo?taskId={Uri.EscapeDataString(taskId)}");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

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
            CallBackUrl = request.UseCallback ? BuildCallbackUrl(request.CorrelationId) : null
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
        if (string.Equals(model, "ideogram/v3-reframe", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["image_url"] = FirstProviderImage(request.ImageInput),
                ["image_size"] = MapAspectRatioToProviderImageSize(request.AspectRatio, allowThreeTwo: false, allowTwentyOneNine: false),
                ["rendering_speed"] = "TURBO",
                ["num_images"] = "1",
                ["style"] = "AUTO"
            };
        }

        if (model.StartsWith("gpt-image/1.5-", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "2:3", "3:2"),
                ["quality"] = NormalizeProviderValue(request.Resolution, "medium", "medium", "high")
            };
            AddProviderImageList(input, request.ImageInput, "input_urls");
            return input;
        }

        if (model.StartsWith("gpt-image-2-", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "auto", "auto", "1:1", "9:16", "16:9", "4:3", "3:4"),
                ["resolution"] = NormalizeProviderValue(request.Resolution, "1K", "1K", "2K", "4K")
            };
            AddProviderImageList(input, request.ImageInput, "input_urls");
            return input;
        }

        if (string.Equals(model, "nano-banana-pro", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model, "nano-banana-2", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9", "auto"),
                ["resolution"] = NormalizeProviderValue(request.Resolution, "1K", "1K", "2K", "4K"),
                ["output_format"] = NormalizeProviderOutputFormat(request.OutputFormat),
                ["number_of_variances"] = request.NumberOfVariances
            };
            AddProviderImageList(input, request.ImageInput, "image_input");
            return input;
        }

        if (model.StartsWith("google/nano-banana", StringComparison.OrdinalIgnoreCase))
        {
            var ratio = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "9:16", "16:9", "3:4", "4:3", "3:2", "2:3", "5:4", "4:5", "21:9", "auto");
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = ratio,
                ["image_size"] = ratio,
                ["output_format"] = NormalizeProviderOutputFormat(request.OutputFormat)
            };
            AddProviderImageList(input, request.ImageInput, "image_urls");
            return input;
        }

        if (model.StartsWith("google/imagen4", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["negative_prompt"] = string.Empty,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "16:9", "9:16", "3:4", "4:3")
            };
            if (!model.EndsWith("-ultra", StringComparison.OrdinalIgnoreCase))
            {
                input["num_images"] = "1";
            }
            return input;
        }

        if (model.StartsWith("bytedance/seedream-v4-", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["image_size"] = MapAspectRatioToProviderImageSize(request.AspectRatio, allowThreeTwo: true, allowTwentyOneNine: true),
                ["image_resolution"] = NormalizeProviderValue(request.Resolution, "1K", "1K", "2K", "4K"),
                ["max_images"] = 1,
                ["nsfw_checker"] = false
            };
            AddProviderImageList(input, request.ImageInput, "image_urls");
            return input;
        }

        if (string.Equals(model, "bytedance/seedream", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["image_size"] = MapAspectRatioToProviderImageSize(request.AspectRatio, allowThreeTwo: false, allowTwentyOneNine: false),
                ["guidance_scale"] = 2.5
            };
        }

        if (model.StartsWith("seedream/4.5-", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("seedream/5-", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "4:3", "3:4", "16:9", "9:16", "2:3", "3:2", "21:9"),
                ["quality"] = NormalizeProviderValue(request.Resolution, "basic", "basic", "high"),
                ["nsfw_checker"] = false
            };
            AddProviderImageList(input, request.ImageInput, "image_urls");
            return input;
        }

        if (model.StartsWith("flux-2/", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "4:3", "3:4", "16:9", "9:16", "3:2", "2:3"),
                ["resolution"] = NormalizeProviderValue(request.Resolution, "1K", "1K", "2K"),
                ["nsfw_checker"] = false
            };
            AddProviderImageList(input, request.ImageInput, "input_urls");
            return input;
        }

        if (model.StartsWith("grok-imagine/", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "2:3", "3:2", "1:1", "16:9", "9:16"),
                ["enable_pro"] = false,
                ["nsfw_checker"] = false
            };
            AddProviderImageList(input, request.ImageInput, "image_urls");
            return input;
        }

        if (model.StartsWith("ideogram/", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["image_size"] = MapAspectRatioToProviderImageSize(request.AspectRatio, allowThreeTwo: false, allowTwentyOneNine: false),
                ["rendering_speed"] = "TURBO",
                ["style"] = "AUTO",
                ["negative_prompt"] = string.Empty,
                ["expand_prompt"] = true
            };
            AddProviderFirstImage(input, request.ImageInput, "image_url");
            if (model.EndsWith("/v3-remix", StringComparison.OrdinalIgnoreCase))
            {
                input["strength"] = 0.5;
                input["num_images"] = "1";
            }
            return input;
        }

        if (model.StartsWith("qwen/", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["image_size"] = MapAspectRatioToProviderImageSize(request.AspectRatio, allowThreeTwo: false, allowTwentyOneNine: false),
                ["output_format"] = NormalizeProviderOutputFormat(request.OutputFormat),
                ["negative_prompt"] = string.Empty,
                ["acceleration"] = "regular",
                ["guidance_scale"] = 2.5,
                ["num_inference_steps"] = 30,
                ["enable_safety_checker"] = true,
                ["nsfw_checker"] = false
            };
            AddProviderFirstImage(input, request.ImageInput, "image_url");
            if (model.EndsWith("/image-to-image", StringComparison.OrdinalIgnoreCase))
            {
                input.Remove("image_size");
                input["strength"] = 0.7;
            }
            else if (model.EndsWith("/image-edit", StringComparison.OrdinalIgnoreCase))
            {
                input["num_images"] = "1";
                input["sync_mode"] = false;
            }
            return input;
        }

        if (model.StartsWith("qwen2/", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["image_size"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "2:3", "3:2", "3:4", "4:3", "9:16", "16:9", "21:9"),
                ["output_format"] = NormalizeProviderOutputFormat(request.OutputFormat),
                ["nsfw_checker"] = false
            };
            AddProviderFirstImage(input, request.ImageInput, "image_url");
            return input;
        }

        if (model.StartsWith("wan/2-7-image", StringComparison.OrdinalIgnoreCase))
        {
            var input = new Dictionary<string, object?>
            {
                ["prompt"] = request.Prompt,
                ["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "16:9", "4:3", "21:9", "3:4", "9:16", "8:1", "1:8"),
                ["resolution"] = NormalizeProviderValue(request.Resolution, "2K", "1K", "2K", "4K"),
                ["n"] = 1,
                ["watermark"] = false,
                ["nsfw_checker"] = false
            };
            AddProviderImageList(input, request.ImageInput, "input_urls");
            return input;
        }

        var fallbackInput = new Dictionary<string, object?> { ["prompt"] = request.Prompt };
        AddProviderImageList(fallbackInput, request.ImageInput, "image_input");
        fallbackInput["aspect_ratio"] = NormalizeProviderRatio(request.AspectRatio, "1:1", "1:1", "4:3", "3:4", "16:9", "9:16");
        fallbackInput["output_format"] = NormalizeProviderOutputFormat(request.OutputFormat);
        return fallbackInput;
    }

    private static string? FirstProviderImage(IReadOnlyList<string>? imageUrls)
        => imageUrls?.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

    private static void AddProviderFirstImage(Dictionary<string, object?> input, IReadOnlyList<string>? imageUrls, string fieldName)
    {
        var first = FirstProviderImage(imageUrls);
        if (!string.IsNullOrWhiteSpace(first))
        {
            input[fieldName] = first;
        }
    }

    private static void AddProviderImageList(Dictionary<string, object?> input, IReadOnlyList<string>? imageUrls, string fieldName)
    {
        var urls = imageUrls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls is { Count: > 0 })
        {
            input[fieldName] = urls;
        }
    }

    private static string NormalizeProviderValue(string? value, string fallback, params string[] supported)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            supported.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return supported.First(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        return fallback;
    }

    private static string NormalizeProviderOutputFormat(string? value)
        => value?.Trim().Equals("jpeg", StringComparison.OrdinalIgnoreCase) == true ||
           value?.Trim().Equals("jpg", StringComparison.OrdinalIgnoreCase) == true
            ? "jpeg"
            : "png";

    private static string NormalizeProviderRatio(string? aspectRatio, string fallback, params string[] supported)
    {
        var normalized = string.IsNullOrWhiteSpace(aspectRatio)
            ? fallback
            : aspectRatio.Trim().ToLowerInvariant();

        if (supported.Any(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return supported.First(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        var mapped = normalized switch
        {
            "5:4" => "4:3",
            "4:5" => "3:4",
            "21:9" or "4:1" or "8:1" => "16:9",
            "1:4" or "1:8" => "9:16",
            "3:2" => supported.Contains("3:2") ? "3:2" : "4:3",
            "2:3" => supported.Contains("2:3") ? "2:3" : "3:4",
            "auto" => supported.Contains("auto") ? "auto" : fallback,
            _ => fallback
        };

        return supported.Any(item => item.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            ? supported.First(item => item.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            : fallback;
    }

    private static string MapAspectRatioToProviderImageSize(
        string? aspectRatio,
        bool allowThreeTwo,
        bool allowTwentyOneNine)
    {
        return aspectRatio switch
        {
            "1:1" => "square_hd",
            "16:9" => "landscape_16_9",
            "9:16" => "portrait_16_9",
            "4:3" => "landscape_4_3",
            "3:4" => "portrait_4_3",
            "3:2" => allowThreeTwo ? "landscape_3_2" : "landscape_4_3",
            "2:3" => allowThreeTwo ? "portrait_3_2" : "portrait_4_3",
            "5:4" => "landscape_4_3",
            "4:5" => "portrait_4_3",
            "21:9" => allowTwentyOneNine ? "landscape_21_9" : "landscape_16_9",
            _ => "square_hd"
        };
    }

    private static string NormalizeQuality(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string NormalizeImageResolution(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "1K" => "1K",
            "2K" => "2K",
            "4K" => "4K",
            _ => "1K"
        };
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
