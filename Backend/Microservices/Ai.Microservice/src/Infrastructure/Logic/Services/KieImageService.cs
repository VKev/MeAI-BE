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

        var payload = new KieCreateTaskRequest
        {
            Model = "nano-banana-pro",
            Input = new KieInputParams
            {
                Prompt = request.Prompt,
                ImageInput = request.ImageInput ?? new List<string>(),
                AspectRatio = request.AspectRatio,
                Resolution = request.Resolution,
                OutputFormat = request.OutputFormat
            },
            CallBackUrl = BuildCallbackUrl(request.CorrelationId)
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/jobs/createTask");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
            httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

            _logger.LogInformation("Sending image generation request to Kie API (nano-banana-pro)");

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

        if (correlationId.HasValue)
        {
            var baseUrl = _options.CallbackUrl.TrimEnd('/');
            var imageCallbackUrl = baseUrl.Replace("/veo/", "/image/");
            if (imageCallbackUrl == baseUrl)
            {
                imageCallbackUrl = $"{baseUrl.TrimEnd('/')}-image";
            }
            return $"{imageCallbackUrl}/{correlationId.Value}";
        }

        return _options.CallbackUrl;
    }

    #region Private API Models

    private sealed class KieCreateTaskRequest
    {
        public string Model { get; set; } = "nano-banana-pro";
        public KieInputParams Input { get; set; } = null!;
        public string? CallBackUrl { get; set; }
    }

    private sealed class KieInputParams
    {
        public string Prompt { get; set; } = null!;

        [JsonPropertyName("image_input")]
        public List<string> ImageInput { get; set; } = new();

        [JsonPropertyName("aspect_ratio")]
        public string AspectRatio { get; set; } = "1:1";

        public string Resolution { get; set; } = "1K";

        [JsonPropertyName("output_format")]
        public string OutputFormat { get; set; } = "png";
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
