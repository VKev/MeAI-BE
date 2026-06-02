using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Application.Abstractions.ApiCredentials;
using Infrastructure.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.Services;

public sealed class VeoVideoService : IVeoVideoService
{
    private readonly HttpClient _httpClient;
    private readonly VeoOptions _options;
    private readonly ILogger<VeoVideoService> _logger;
    private readonly IApiCredentialProvider _credentialProvider;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public VeoVideoService(
        HttpClient httpClient,
        IOptions<VeoOptions> options,
        IApiCredentialProvider credentialProvider,
        ILogger<VeoVideoService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _credentialProvider = credentialProvider;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<VeoGenerateResult> GenerateVideoAsync(
        VeoGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialProvider.GetOptionalValue("Kie", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new VeoGenerateResult(false, 401, "Veo API key is not configured", null);
        }

        var isVeoModel = request.Model.StartsWith("veo", StringComparison.OrdinalIgnoreCase);
        var callbackUrl = BuildCallbackUrl(request.CorrelationId);

        object payload;
        string endpoint;

        if (isVeoModel)
        {
            endpoint = "/api/v1/veo/generate";
            payload = new VeoApiRequest
            {
                Prompt = request.Prompt,
                ImageUrls = request.ImageUrls,
                Model = ResolveVeoApiModel(request.Model, request.Variant),
                GenerationType = request.GenerationType,
                AspectRatio = NormalizeVeoAspectRatio(request.AspectRatio),
                Seeds = request.Seeds,
                EnableTranslation = request.EnableTranslation,
                Watermark = request.Watermark,
                CallBackUrl = callbackUrl
            };
        }
        else
        {
            // Market models use the unified createTask endpoint
            endpoint = "/api/v1/jobs/createTask";
            var input = BuildMarketVideoInput(request);
            payload = new { model = request.Model, input, callBackUrl = callbackUrl };
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

            _logger.LogInformation("Sending video generation request to Veo API");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogDebug("Veo API response: {StatusCode} - {Content}", response.StatusCode, content);

            var apiResponse = JsonSerializer.Deserialize<VeoApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                _logger.LogError("Failed to deserialize Veo API response");
                return new VeoGenerateResult(false, 500, "Failed to parse API response", null);
            }

            if (apiResponse.Code == 200 && apiResponse.Data?.TaskId is not null)
            {
                _logger.LogInformation("Video generation task created: {TaskId}", apiResponse.Data.TaskId);
                return new VeoGenerateResult(true, 200, apiResponse.Msg ?? "Success", apiResponse.Data.TaskId);
            }

            _logger.LogWarning("Veo API returned error: {Code} - {Message}", apiResponse.Code, apiResponse.Msg);
            return new VeoGenerateResult(false, apiResponse.Code, apiResponse.Msg ?? "Unknown error", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while calling Veo API");
            return new VeoGenerateResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calling Veo API");
            return new VeoGenerateResult(false, 500, $"Unexpected error: {ex.Message}", null);
        }
    }

    public async Task<VeoExtendResult> ExtendVideoAsync(
        VeoExtendRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialProvider.GetOptionalValue("Kie", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new VeoExtendResult(false, 401, "Veo API key is not configured", null);
        }

        var payload = new VeoExtendApiRequest
        {
            TaskId = request.TaskId,
            Prompt = request.Prompt,
            Seeds = request.Seeds,
            Watermark = request.Watermark,
            CallBackUrl = BuildCallbackUrl(request.CorrelationId)
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/veo/extend");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

            _logger.LogInformation("Sending video extension request to Veo API for task {TaskId}", request.TaskId);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogDebug("Veo API response: {StatusCode} - {Content}", response.StatusCode, content);

            var apiResponse = JsonSerializer.Deserialize<VeoApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                _logger.LogError("Failed to deserialize Veo API response");
                return new VeoExtendResult(false, 500, "Failed to parse API response", null);
            }

            if (apiResponse.Code == 200 && apiResponse.Data?.TaskId is not null)
            {
                _logger.LogInformation("Video extension task created: {TaskId}", apiResponse.Data.TaskId);
                return new VeoExtendResult(true, 200, apiResponse.Msg ?? "Success", apiResponse.Data.TaskId);
            }

            _logger.LogWarning("Veo API returned error: {Code} - {Message}", apiResponse.Code, apiResponse.Msg);
            return new VeoExtendResult(false, apiResponse.Code, apiResponse.Msg ?? "Unknown error", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while calling Veo API");
            return new VeoExtendResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calling Veo API");
            return new VeoExtendResult(false, 500, $"Unexpected error: {ex.Message}", null);
        }
    }

    public async Task<VeoRecordInfoResult> GetVideoDetailsAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialProvider.GetOptionalValue("Kie", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new VeoRecordInfoResult(false, 401, "Veo API key is not configured", null);
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/veo/record-info?taskId={Uri.EscapeDataString(taskId)}");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            _logger.LogInformation("Querying video details for task {TaskId}", taskId);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogDebug("Veo API response: {StatusCode} - {Content}", response.StatusCode, content);

            var apiResponse = JsonSerializer.Deserialize<VeoRecordInfoApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                _logger.LogError("Failed to deserialize Veo API response");
                return new VeoRecordInfoResult(false, 500, "Failed to parse API response", null);
            }

            if (apiResponse.Code == 200 && apiResponse.Data is not null)
            {
                var data = apiResponse.Data;
                var recordInfo = new VeoRecordInfo(
                    TaskId: data.TaskId ?? taskId,
                    ParamJson: data.ParamJson,
                    CompleteTime: data.CompleteTime,
                    Response: data.Response is not null
                        ? new VeoRecordResponse(
                            TaskId: data.Response.TaskId ?? taskId,
                            ResultUrls: data.Response.ResultUrls,
                            OriginUrls: data.Response.OriginUrls,
                            Resolution: data.Response.Resolution)
                        : null,
                    SuccessFlag: data.SuccessFlag,
                    ErrorCode: data.ErrorCode,
                    ErrorMessage: data.ErrorMessage,
                    CreateTime: data.CreateTime,
                    FallbackFlag: data.FallbackFlag);

                _logger.LogInformation("Video details retrieved for task {TaskId}, status: {SuccessFlag}", taskId, data.SuccessFlag);
                return new VeoRecordInfoResult(true, 200, apiResponse.Msg ?? "Success", recordInfo);
            }

            _logger.LogWarning("Veo API returned error: {Code} - {Message}", apiResponse.Code, apiResponse.Msg);
            return new VeoRecordInfoResult(false, apiResponse.Code, apiResponse.Msg ?? "Unknown error", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while calling Veo API");
            return new VeoRecordInfoResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calling Veo API");
            return new VeoRecordInfoResult(false, 500, $"Unexpected error: {ex.Message}", null);
        }
    }

    public async Task<Veo1080PResult> Get1080PVideoAsync(
        string taskId,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialProvider.GetOptionalValue("Kie", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new Veo1080PResult(false, 401, "Veo API key is not configured", null);
        }

        try
        {
            var url = $"/api/v1/veo/get-1080p-video?taskId={Uri.EscapeDataString(taskId)}&index={index}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            _logger.LogInformation("Requesting 1080P video for task {TaskId}, index {Index}", taskId, index);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogDebug("Veo API response: {StatusCode} - {Content}", response.StatusCode, content);

            var apiResponse = JsonSerializer.Deserialize<Veo1080PApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                _logger.LogError("Failed to deserialize Veo API response");
                return new Veo1080PResult(false, 500, "Failed to parse API response", null);
            }

            if (apiResponse.Code == 200 && apiResponse.Data?.ResultUrl is not null)
            {
                _logger.LogInformation("1080P video retrieved for task {TaskId}: {Url}", taskId, apiResponse.Data.ResultUrl);
                return new Veo1080PResult(true, 200, apiResponse.Msg ?? "Success", apiResponse.Data.ResultUrl);
            }

            _logger.LogWarning("Veo API returned error: {Code} - {Message}", apiResponse.Code, apiResponse.Msg);
            return new Veo1080PResult(false, apiResponse.Code, apiResponse.Msg ?? "Unknown error", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while calling Veo API");
            return new Veo1080PResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calling Veo API");
            return new Veo1080PResult(false, 500, $"Unexpected error: {ex.Message}", null);
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
            return $"{callbackBaseUrl}/{correlationId.Value:D}?provider=video";
        }

        return $"{callbackBaseUrl}?provider=video";
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

    private static string NormalizeVeoAspectRatio(string? aspectRatio)
    {
        // Veo accepts: "16:9", "9:16", "Auto" (capital A)
        if (string.IsNullOrWhiteSpace(aspectRatio)) return "16:9";
        return aspectRatio.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "Auto"
            : aspectRatio;
    }

    private static string ResolveVeoApiModel(string model, string? variant)
    {
        if (!string.Equals(model, "veo-3-1", StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        return variant?.Trim().ToLowerInvariant() switch
        {
            "lite" => "veo3_lite",
            "quality" => "veo3",
            _ => "veo3_fast"
        };
    }

    private static Dictionary<string, object?> BuildMarketVideoInput(VeoGenerateRequest request)
    {
        var model = request.Model;
        var input = new Dictionary<string, object?> { ["prompt"] = request.Prompt };

        if (model.StartsWith("sora-2", StringComparison.OrdinalIgnoreCase))
        {
            input["aspect_ratio"] = request.AspectRatio == "9:16" ? "portrait" : "landscape";
            input["n_frames"] = "10";
            input["remove_watermark"] = true;
            input["upload_method"] = "s3";
            AddProviderVideoImages(input, request.ImageUrls, "image_urls");
            return input;
        }

        if (string.Equals(model, "grok-imagine-video-1-5-preview", StringComparison.OrdinalIgnoreCase))
        {
            input["aspect_ratio"] = string.Equals(request.AspectRatio, "auto", StringComparison.OrdinalIgnoreCase)
                ? "auto"
                : NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3");
            input["resolution"] = NormalizeProviderVideoResolution(request.Resolution, "480p", "480p", "720p");
            input["duration"] = NormalizeProviderVideoDuration(request.Duration, 8, 1, 15);
            AddProviderVideoFirstImageAsList(input, request.ImageUrls, "image_urls");
            return input;
        }

        if (model.StartsWith("grok-imagine/", StringComparison.OrdinalIgnoreCase))
        {
            input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "2:3", "3:2", "1:1", "16:9", "9:16");
            input["resolution"] = "720p";
            input["duration"] = 5;
            input["mode"] = "normal";
            input["nsfw_checker"] = false;
            AddProviderVideoImages(input, request.ImageUrls, "image_urls");
            return input;
        }

        if (model.StartsWith("kling-3.0", StringComparison.OrdinalIgnoreCase))
        {
            input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1");
            input["duration"] = 5;
            input["sound"] = false;
            input["mode"] = "std";
            input["multi_shots"] = false;
            AddProviderVideoImages(input, request.ImageUrls, "image_urls");
            return input;
        }

        if (model.StartsWith("kling-2.6", StringComparison.OrdinalIgnoreCase))
        {
            input["duration"] = 5;
            input["sound"] = false;
            if (model.EndsWith("/text-to-video", StringComparison.OrdinalIgnoreCase))
            {
                input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "1:1", "16:9", "9:16");
            }
            AddProviderVideoImages(input, request.ImageUrls, "image_urls");
            return input;
        }

        if (model.StartsWith("kling/v2-", StringComparison.OrdinalIgnoreCase))
        {
            input["duration"] = 5;
            input["cfg_scale"] = 0.5;
            input["negative_prompt"] = string.Empty;
            if (model.Contains("text-to-video", StringComparison.OrdinalIgnoreCase))
            {
                input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1");
            }
            else
            {
                AddProviderVideoFirstImage(input, request.ImageUrls, "image_url");
            }
            return input;
        }

        if (model.StartsWith("bytedance/seedance", StringComparison.OrdinalIgnoreCase))
        {
            input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "1:1", "4:3", "3:4", "16:9", "9:16", "21:9");
            input["resolution"] = NormalizeProviderVideoResolution(request.Resolution, "720p", "480p", "720p", "1080p");
            input["duration"] = NormalizeProviderVideoDuration(request.Duration, 5, 4, 15);
            input["generate_audio"] = request.GenerateAudio ?? false;
            input["nsfw_checker"] = false;
            if (model.StartsWith("bytedance/seedance-1.5", StringComparison.OrdinalIgnoreCase))
            {
                input["fixed_lens"] = false;
                AddProviderVideoImages(input, request.ImageUrls, "input_urls");
            }
            else
            {
                input["return_last_frame"] = request.ReturnLastFrame ?? false;
                input["web_search"] = request.WebSearch ?? false;
                AddSeedance2Images(input, request.ImageUrls, request.GenerationType);
            }
            return input;
        }

        if (model.StartsWith("bytedance/v1-", StringComparison.OrdinalIgnoreCase))
        {
            input["duration"] = 5;
            input["resolution"] = "720p";
            input["camera_fixed"] = false;
            input["enable_safety_checker"] = true;
            input["nsfw_checker"] = false;
            if (model.Contains("text-to-video", StringComparison.OrdinalIgnoreCase))
            {
                input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "21:9", "16:9", "4:3", "1:1", "3:4", "9:16");
            }
            else
            {
                AddProviderVideoFirstImage(input, request.ImageUrls, "image_url");
            }
            AddProviderVideoSeed(input, request.Seeds);
            return input;
        }

        if (model.StartsWith("hailuo/", StringComparison.OrdinalIgnoreCase))
        {
            input["prompt_optimizer"] = true;
            input["nsfw_checker"] = false;
            if (model.Contains("image-to-video", StringComparison.OrdinalIgnoreCase))
            {
                AddProviderVideoFirstImage(input, request.ImageUrls, "image_url");
                if (model.Contains("2-3-", StringComparison.OrdinalIgnoreCase))
                {
                    input["duration"] = 6;
                    input["resolution"] = "768P";
                }
            }
            else if (model.Contains("standard", StringComparison.OrdinalIgnoreCase))
            {
                input["duration"] = 6;
            }
            return input;
        }

        if (model.StartsWith("wan/", StringComparison.OrdinalIgnoreCase))
        {
            input["duration"] = 5;
            input["resolution"] = "1080p";
            input["nsfw_checker"] = false;

            if (model.StartsWith("wan/2-7-text", StringComparison.OrdinalIgnoreCase))
            {
                input["ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1", "4:3", "3:4");
                input["prompt_extend"] = true;
                input["watermark"] = false;
                AddProviderVideoSeed(input, request.Seeds);
                return input;
            }

            if (model.StartsWith("wan/2-7-image", StringComparison.OrdinalIgnoreCase))
            {
                input["prompt_extend"] = true;
                input["watermark"] = false;
                AddProviderVideoFirstImage(input, request.ImageUrls, "first_frame_url");
                AddProviderVideoSeed(input, request.Seeds);
                return input;
            }

            if (model.StartsWith("wan/2-7-r2v", StringComparison.OrdinalIgnoreCase))
            {
                input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1", "4:3", "3:4");
                input["prompt_extend"] = true;
                input["watermark"] = false;
                AddProviderVideoFirstImage(input, request.ImageUrls, "reference_image");
                AddProviderVideoSeed(input, request.Seeds);
                return input;
            }

            if (model.StartsWith("wan/2-6", StringComparison.OrdinalIgnoreCase))
            {
                AddProviderVideoImages(input, request.ImageUrls, "image_urls");
                return input;
            }

            if (model.StartsWith("wan/2-5", StringComparison.OrdinalIgnoreCase))
            {
                input["enable_prompt_expansion"] = true;
                input["negative_prompt"] = string.Empty;
                input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1");
                AddProviderVideoFirstImage(input, request.ImageUrls, "image_url");
                AddProviderVideoSeed(input, request.Seeds);
                return input;
            }

            if (model.StartsWith("wan/2-2", StringComparison.OrdinalIgnoreCase))
            {
                input["resolution"] = "720p";
                input["enable_prompt_expansion"] = true;
                input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16");
                AddProviderVideoFirstImage(input, request.ImageUrls, "image_url");
                AddProviderVideoSeed(input, request.Seeds);
                return input;
            }
        }

        if (model.StartsWith("happyhorse/", StringComparison.OrdinalIgnoreCase))
        {
            input["duration"] = 5;
            input["resolution"] = "1080p";
            input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1", "4:3", "3:4");
            AddProviderVideoFirstImage(input, request.ImageUrls, "first_frame");
            AddProviderVideoFirstImage(input, request.ImageUrls, "reference_image");
            AddProviderVideoSeed(input, request.Seeds);
            return input;
        }

        if (string.Equals(model, "gemini-omni-video", StringComparison.OrdinalIgnoreCase))
        {
            input["duration"] = NormalizeProviderVideoDurationOption(request.Duration, 4, 4, 6, 8, 10).ToString();
            input["resolution"] = NormalizeProviderVideoResolution(request.Resolution, "720p", "720p", "1080p", "4k");
            input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16");
            AddProviderVideoImages(input, request.ImageUrls, "image_urls");
            AddProviderVideoSeed(input, request.Seeds);
            return input;
        }

        input["aspect_ratio"] = NormalizeProviderVideoRatio(request.AspectRatio, "16:9", "16:9", "9:16", "1:1");
        AddProviderVideoFirstImage(input, request.ImageUrls, "image_url");
        return input;
    }

    private static void AddSeedance2Images(
        Dictionary<string, object?> input,
        IReadOnlyList<string>? imageUrls,
        string? generationType)
    {
        var urls = imageUrls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls is not { Count: > 0 })
        {
            return;
        }

        if (string.Equals(generationType, "REFERENCE_2_VIDEO", StringComparison.OrdinalIgnoreCase))
        {
            input["reference_image_urls"] = urls.Take(9).ToList();
            return;
        }

        if (urls.Count > 0)
        {
            input["first_frame_url"] = urls[0];
            if (urls.Count >= 2)
            {
                input["last_frame_url"] = urls[1];
            }

            return;
        }
    }

    private static void AddProviderVideoFirstImage(
        Dictionary<string, object?> input,
        IReadOnlyList<string>? imageUrls,
        string fieldName)
    {
        var first = imageUrls?.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        if (!string.IsNullOrWhiteSpace(first))
        {
            input[fieldName] = first;
        }
    }

    private static void AddProviderVideoImages(
        Dictionary<string, object?> input,
        IReadOnlyList<string>? imageUrls,
        string fieldName)
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

    private static void AddProviderVideoFirstImageAsList(
        Dictionary<string, object?> input,
        IReadOnlyList<string>? imageUrls,
        string fieldName)
    {
        var first = imageUrls?.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        if (!string.IsNullOrWhiteSpace(first))
        {
            input[fieldName] = new List<string> { first };
        }
    }

    private static void AddProviderVideoSeed(Dictionary<string, object?> input, int? seed)
    {
        if (seed.HasValue)
        {
            input["seed"] = seed.Value;
        }
    }

    private static string NormalizeProviderVideoRatio(string? aspectRatio, string fallback, params string[] supported)
    {
        var normalized = string.IsNullOrWhiteSpace(aspectRatio) || aspectRatio.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : aspectRatio.Trim();

        if (supported.Any(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return supported.First(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        var mapped = normalized switch
        {
            "21:9" => supported.Contains("21:9") ? "21:9" : "16:9",
            "4:5" => supported.Contains("3:4") ? "3:4" : "9:16",
            "5:4" => supported.Contains("4:3") ? "4:3" : "16:9",
            "2:3" => supported.Contains("2:3") ? "2:3" : "9:16",
            "3:2" => supported.Contains("3:2") ? "3:2" : "16:9",
            _ => fallback
        };

        return supported.Any(item => item.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            ? supported.First(item => item.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            : fallback;
    }

    private static string NormalizeProviderVideoResolution(string? resolution, string fallback, params string[] supported)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return fallback;
        }

        return supported.FirstOrDefault(item =>
            item.Equals(resolution.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static int NormalizeProviderVideoDuration(int? duration, int fallback, int minimum, int maximum)
    {
        return duration.HasValue
            ? Math.Clamp(duration.Value, minimum, maximum)
            : fallback;
    }

    private static int NormalizeProviderVideoDurationOption(int? duration, int fallback, params int[] supported)
    {
        return duration.HasValue && supported.Contains(duration.Value)
            ? duration.Value
            : fallback;
    }

    private static void AddImageUrls(
        Dictionary<string, object?> input,
        IReadOnlyList<string>? imageUrls,
        string fieldName)
    {
        if (imageUrls is not { Count: > 0 })
        {
            return;
        }

        input[fieldName] = fieldName.EndsWith("urls", StringComparison.OrdinalIgnoreCase)
            ? imageUrls
            : imageUrls[0];
    }

    private static string NormalizeMarketAspectRatio(string? aspectRatio)
    {
        return string.IsNullOrWhiteSpace(aspectRatio) || aspectRatio.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "16:9"
            : aspectRatio;
    }

    private static string MapSoraAspectRatio(string? aspectRatio)
    {
        return aspectRatio switch
        {
            "9:16" => "portrait",
            _ => "landscape"
        };
    }

    private sealed class VeoApiRequest
    {
        public required string Prompt { get; set; }

        [JsonPropertyName("imageUrls")]
        public List<string>? ImageUrls { get; set; }

        public string Model { get; set; } = "veo3_fast";

        [JsonPropertyName("generationType")]
        public string? GenerationType { get; set; }

        [JsonPropertyName("aspect_ratio")]
        public string AspectRatio { get; set; } = "16:9";

        public int? Seeds { get; set; }

        public bool EnableTranslation { get; set; } = true;

        public string? Watermark { get; set; }

        public string? CallBackUrl { get; set; }
    }

    private sealed class VeoExtendApiRequest
    {
        public required string TaskId { get; set; }
        public required string Prompt { get; set; }
        public int? Seeds { get; set; }
        public string? Watermark { get; set; }
        public string? CallBackUrl { get; set; }
    }

    private sealed class VeoApiResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public VeoApiResponseData? Data { get; set; }
    }

    private sealed class VeoApiResponseData
    {
        public string? TaskId { get; set; }
    }

    private sealed class VeoRecordInfoApiResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public VeoRecordInfoData? Data { get; set; }
    }

    private sealed class VeoRecordInfoData
    {
        public string? TaskId { get; set; }
        public string? ParamJson { get; set; }

        [JsonConverter(typeof(UnixTimestampToDateTimeConverter))]
        public DateTime? CompleteTime { get; set; }
        public VeoRecordInfoResponseData? Response { get; set; }
        public int SuccessFlag { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        [JsonConverter(typeof(UnixTimestampToDateTimeConverter))]
        public DateTime? CreateTime { get; set; }
        public bool FallbackFlag { get; set; }
    }

    private sealed class UnixTimestampToDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                // Unix timestamp in milliseconds
                var timestamp = reader.GetInt64();
                return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (string.IsNullOrEmpty(stringValue))
                {
                    return null;
                }

                if (DateTime.TryParse(stringValue, out var dateTime))
                {
                    return dateTime;
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(new DateTimeOffset(value.Value).ToUnixTimeMilliseconds());
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private sealed class VeoRecordInfoResponseData
    {
        public string? TaskId { get; set; }
        public List<string>? ResultUrls { get; set; }
        public List<string>? OriginUrls { get; set; }
        public string? Resolution { get; set; }
    }

    private sealed class Veo1080PApiResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public Veo1080PData? Data { get; set; }
    }

    private sealed class Veo1080PData
    {
        public string? ResultUrl { get; set; }
    }
}
