using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Automation;
using Infrastructure.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Automation;

public sealed class N8nWorkflowClient : IN8nWorkflowClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly N8nOptions _options;
    private readonly IApiCredentialProvider _credentialProvider;
    private readonly ILogger<N8nWorkflowClient> _logger;

    public N8nWorkflowClient(
        IHttpClientFactory httpClientFactory,
        IOptions<N8nOptions> options,
        IApiCredentialProvider credentialProvider,
        ILogger<N8nWorkflowClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("n8n");
        _options = options.Value;
        _credentialProvider = credentialProvider;
        _logger = logger;
    }

    public async Task<Result<N8nScheduledAgentJobAck>> RegisterScheduledAgentJobAsync(
        N8nScheduledAgentJobRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            jobId = request.JobId,
            scheduleId = request.ScheduleId,
            userId = request.UserId,
            workspaceId = request.WorkspaceId,
            executeAtUtc = request.ExecuteAtUtc,
            timezone = request.Timezone,
            search = new
            {
                queryTemplate = request.Search.QueryTemplate,
                country = request.Search.Country,
                searchLang = request.Search.SearchLanguage,
                count = request.Search.Count,
                freshness = request.Search.Freshness
            },
            callback = new
            {
                url = BuildRuntimeCallbackUrl(),
                token = _credentialProvider.GetRequiredValue("N8n", "InternalCallbackToken")
            }
        };

        using var requestCts = CreateTimeoutTokenSource(_options.RegisterTimeoutSeconds, cancellationToken);
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                BuildUrl(_options.ScheduledAgentJobPath),
                payload,
                JsonOptions,
                requestCts.Token);

            var body = await response.Content.ReadAsStringAsync(requestCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<N8nScheduledAgentJobAck>(
                    new Error("N8n.RegisterFailed", $"n8n scheduled agent job registration failed with status {(int)response.StatusCode}: {body}"));
            }

            var parsed = TryDeserialize<N8nAckPayload>(body);
            return Result.Success(new N8nScheduledAgentJobAck(
                parsed?.ExecutionId,
                parsed?.AcceptedAtUtc ?? DateTime.UtcNow));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "n8n scheduled job registration timed out after {TimeoutSeconds}s. BaseUrl: {BaseUrl}",
                _options.RegisterTimeoutSeconds,
                _options.BaseUrl);
            return Result.Failure<N8nScheduledAgentJobAck>(
                new Error("N8n.RegisterTimeout", $"n8n scheduled agent job registration timed out after {_options.RegisterTimeoutSeconds} seconds."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "n8n scheduled job registration failed to reach {BaseUrl}", _options.BaseUrl);
            return Result.Failure<N8nScheduledAgentJobAck>(
                new Error("N8n.Unreachable", $"n8n is unreachable at {_options.BaseUrl}. {ex.Message}"));
        }
    }

    public async Task<Result<N8nWebSearchResponse>> WebSearchAsync(
        N8nWebSearchRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            queryTemplate = request.QueryTemplate,
            count = request.Count,
            country = request.Country,
            searchLang = request.SearchLanguage,
            freshness = request.Freshness,
            timezone = request.Timezone,
            executeAtUtc = request.ExecuteAtUtc
        };

        using var requestCts = CreateTimeoutTokenSource(_options.WebSearchTimeoutSeconds, cancellationToken);
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                BuildUrl(_options.WebSearchPath),
                payload,
                JsonOptions,
                requestCts.Token);

            var body = await response.Content.ReadAsStringAsync(requestCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<N8nWebSearchResponse>(
                    new Error("N8n.WebSearchFailed", $"n8n web search failed with status {(int)response.StatusCode}: {body}"));
            }

            var parsed = TryDeserialize<N8nWebSearchPayload>(body);
            if (parsed is null)
            {
                return Result.Failure<N8nWebSearchResponse>(
                    new Error("N8n.WebSearchInvalidResponse", "n8n web search response could not be parsed."));
            }

            return Result.Success(new N8nWebSearchResponse(
                parsed.Query ?? request.QueryTemplate,
                parsed.RetrievedAtUtc ?? DateTime.UtcNow,
                parsed.Results?.Select(item => new N8nWebSearchResultItem(
                    item.Title,
                    item.Url,
                    item.Description,
                    item.Source)).ToList() ?? [],
                parsed.LlmContext));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "n8n web search timed out after {TimeoutSeconds}s. BaseUrl: {BaseUrl}",
                _options.WebSearchTimeoutSeconds,
                _options.BaseUrl);
            return Result.Failure<N8nWebSearchResponse>(
                new Error("N8n.WebSearchTimeout", $"n8n web search timed out after {_options.WebSearchTimeoutSeconds} seconds."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "n8n web search failed to reach {BaseUrl}", _options.BaseUrl);
            return Result.Failure<N8nWebSearchResponse>(
                new Error("N8n.Unreachable", $"n8n is unreachable at {_options.BaseUrl}. {ex.Message}"));
        }
    }

    private string BuildRuntimeCallbackUrl()
    {
        return $"{_options.CallbackBaseUrl.TrimEnd('/')}/{_options.RuntimeCallbackPath.TrimStart('/')}";
    }

    private string BuildUrl(string path)
    {
        return $"{_options.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static T? TryDeserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static CancellationTokenSource CreateTimeoutTokenSource(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var safeTimeout = Math.Max(1, timeoutSeconds);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(safeTimeout));
        return linkedCts;
    }

    private sealed class N8nAckPayload
    {
        public string? ExecutionId { get; set; }

        public DateTime? AcceptedAtUtc { get; set; }
    }

    private sealed class N8nWebSearchPayload
    {
        public string? Query { get; set; }

        public DateTime? RetrievedAtUtc { get; set; }

        public List<N8nWebSearchResultPayload>? Results { get; set; }

        public string? LlmContext { get; set; }
    }

    private sealed class N8nWebSearchResultPayload
    {
        public string? Title { get; set; }

        public string? Url { get; set; }

        public string? Description { get; set; }

        public string? Source { get; set; }
    }
}
