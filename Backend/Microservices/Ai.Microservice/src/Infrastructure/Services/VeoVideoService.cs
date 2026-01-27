using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Infrastructure.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class VeoVideoService : IVeoVideoService
{
    private readonly HttpClient _httpClient;
    private readonly VeoOptions _options;
    private readonly ILogger<VeoVideoService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public VeoVideoService(
        HttpClient httpClient,
        IOptions<VeoOptions> options,
        ILogger<VeoVideoService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<VeoGenerateResult> GenerateVideoAsync(
        VeoGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new VeoGenerateResult(false, 401, "Veo API key is not configured", null);
        }

        var payload = new VeoApiRequest
        {
            Prompt = request.Prompt,
            ImageUrls = request.ImageUrls,
            Model = request.Model,
            GenerationType = request.GenerationType,
            AspectRatio = request.AspectRatio,
            Seeds = request.Seeds,
            EnableTranslation = request.EnableTranslation,
            Watermark = request.Watermark,
            CallBackUrl = BuildCallbackUrl(request.CorrelationId)
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/veo/generate");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
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
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
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
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
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
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new VeoRecordInfoResult(false, 401, "Veo API key is not configured", null);
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/veo/record-info?taskId={Uri.EscapeDataString(taskId)}");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

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
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Veo API key is not configured");
            return new Veo1080PResult(false, 401, "Veo API key is not configured", null);
        }

        try
        {
            var url = $"/api/v1/veo/get-1080p-video?taskId={Uri.EscapeDataString(taskId)}&index={index}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

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

        if (correlationId.HasValue)
        {
            var baseUrl = _options.CallbackUrl.TrimEnd('/');
            return $"{baseUrl}/{correlationId.Value}";
        }

        return _options.CallbackUrl;
    }

    private sealed class VeoApiRequest
    {
        public required string Prompt { get; set; }
        public List<string>? ImageUrls { get; set; }
        public string Model { get; set; } = "veo3_fast";
        public string? GenerationType { get; set; }
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
