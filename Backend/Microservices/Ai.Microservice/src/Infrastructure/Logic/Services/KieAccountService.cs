using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Kie;
using Infrastructure.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.Services;

public sealed class KieAccountService : IKieAccountService
{
    private readonly HttpClient _httpClient;
    private readonly VeoOptions _options;
    private readonly ILogger<KieAccountService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public KieAccountService(
        HttpClient httpClient,
        IOptions<VeoOptions> options,
        ILogger<KieAccountService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<KieCreditBalanceResult> GetCreditBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Kie API key is not configured; credit balance cannot be checked.");
            return new KieCreditBalanceResult(false, 401, "Kie API key is not configured", null);
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/chat/credit");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(GetCreditLookupTimeoutSeconds()));

            var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
            var content = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var apiResponse = JsonSerializer.Deserialize<KieCreditApiResponse>(content, JsonOptions);

            if (apiResponse is null)
            {
                return new KieCreditBalanceResult(false, 500, "Failed to parse Kie credit response", null);
            }

            if (apiResponse.Code == 200)
            {
                return new KieCreditBalanceResult(
                    true,
                    apiResponse.Code,
                    apiResponse.Msg ?? "success",
                    apiResponse.Data);
            }

            _logger.LogWarning(
                "Kie credit endpoint returned error {Code}: {Message}",
                apiResponse.Code,
                apiResponse.Msg);

            return new KieCreditBalanceResult(
                false,
                apiResponse.Code,
                apiResponse.Msg ?? $"Kie credit endpoint failed with HTTP {(int)response.StatusCode}",
                null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error while checking Kie credit balance.");
            return new KieCreditBalanceResult(false, 500, $"HTTP error: {ex.Message}", null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Kie credit balance lookup timed out.");
            return new KieCreditBalanceResult(false, 504, "Kie credit balance lookup timed out", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while checking Kie credit balance.");
            return new KieCreditBalanceResult(false, 500, $"Unexpected error: {ex.Message}", null);
        }
    }

    private int GetCreditLookupTimeoutSeconds()
    {
        return _options.CreditLookupTimeoutSeconds <= 0
            ? 5
            : _options.CreditLookupTimeoutSeconds;
    }

    private sealed class KieCreditApiResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public decimal? Data { get; set; }
    }
}
