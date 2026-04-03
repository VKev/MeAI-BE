using System.Net.Http.Json;
using System.Text.Json;
using Application.Kie.Commands;
using Application.Kie.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Services;

public interface IKieFallbackCallbackService
{
    Task<bool> SendImageSuccessCallbackAsync(
        Guid correlationId,
        string kieTaskId,
        int numberOfVariances = 1,
        CancellationToken cancellationToken = default);
}

public sealed class KieFallbackCallbackService : IKieFallbackCallbackService
{
    private const string LocalCallbackBaseUrl = "http://localhost:5001";
    private static readonly TimeSpan SimulatedDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly IAiFallbackTemplateService _fallbackTemplateService;
    private readonly IMediator _mediator;
    private readonly ILogger<KieFallbackCallbackService> _logger;

    public KieFallbackCallbackService(
        HttpClient httpClient,
        IAiFallbackTemplateService fallbackTemplateService,
        IMediator mediator,
        ILogger<KieFallbackCallbackService> logger)
    {
        _httpClient = httpClient;
        _fallbackTemplateService = fallbackTemplateService;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<bool> SendImageSuccessCallbackAsync(
        Guid correlationId,
        string kieTaskId,
        int numberOfVariances = 1,
        CancellationToken cancellationToken = default)
    {
        if (!_fallbackTemplateService.TryGetImageFallback(out var fallbackAsset))
        {
            _logger.LogWarning(
                "Kie fallback callback skipped: image fallback template is unavailable. CorrelationId: {CorrelationId}",
                correlationId);
            return false;
        }

        try
        {
            await Task.Delay(SimulatedDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        var varianceCount = Math.Max(1, numberOfVariances);
        var resultJson = JsonSerializer.Serialize(new KieResultJson(
            Enumerable.Repeat(fallbackAsset.ResultUrl, varianceCount).ToList()));
        var payload = new KieCallbackPayload(
            Code: 200,
            Msg: "Fallback success",
            Data: new KieCallbackData(
                TaskId: kieTaskId,
                State: "success",
                ResultJson: resultJson,
                FailCode: null,
                FailMsg: null,
                CompleteTime: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        var callbackUrl = $"{LocalCallbackBaseUrl}/api/Ai/kie/callback/{correlationId:D}?provider=image";

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                callbackUrl,
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Kie fallback callback via localhost failed. CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, Response: {Response}. Fallbacking to in-process callback command.",
                    correlationId,
                    response.StatusCode,
                    responseBody);
                return await SendInProcessCallbackAsync(correlationId, payload, cancellationToken);
            }

            _logger.LogInformation(
                "Kie fallback callback sent successfully. CorrelationId: {CorrelationId}, CallbackUrl: {CallbackUrl}",
                correlationId,
                callbackUrl);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Kie fallback callback via localhost threw exception. CorrelationId: {CorrelationId}, CallbackUrl: {CallbackUrl}. Fallbacking to in-process callback command.",
                correlationId,
                callbackUrl);
            return await SendInProcessCallbackAsync(correlationId, payload, cancellationToken);
        }
    }

    private async Task<bool> SendInProcessCallbackAsync(
        Guid correlationId,
        KieCallbackPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var commandResult = await _mediator.Send(
                new HandleImageCallbackCommand(correlationId, payload),
                cancellationToken);

            if (commandResult.IsFailure || !commandResult.Value)
            {
                _logger.LogWarning(
                    "In-process fallback callback command returned failure. CorrelationId: {CorrelationId}",
                    correlationId);
                return false;
            }

            _logger.LogInformation(
                "In-process fallback callback command handled successfully. CorrelationId: {CorrelationId}",
                correlationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "In-process fallback callback command failed. CorrelationId: {CorrelationId}",
                correlationId);
            return false;
        }
    }
}
