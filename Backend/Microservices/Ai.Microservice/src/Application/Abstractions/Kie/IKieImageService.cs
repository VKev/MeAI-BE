namespace Application.Abstractions.Kie;

public interface IKieImageService
{
    Task<KieGenerateResult> GenerateImageAsync(KieGenerateRequest request, CancellationToken cancellationToken = default);
    Task<KieRecordInfoResult> GetImageDetailsAsync(string taskId, CancellationToken cancellationToken = default);
}

public sealed record KieGenerateRequest(
    string Prompt,
    List<string>? ImageInput = null,
    string AspectRatio = "1:1",
    string Resolution = "1K",
    string OutputFormat = "png",
    Guid? CorrelationId = null);

public sealed record KieGenerateResult(
    bool Success,
    int Code,
    string Message,
    string? TaskId);

public sealed record KieRecordInfoResult(
    bool Success,
    int Code,
    string Message,
    KieRecordInfo? Data);

public sealed record KieRecordInfo(
    string TaskId,
    string? Model,
    string State,
    string? ParamJson,
    string? ResultJson,
    string? FailCode,
    string? FailMsg,
    long? CostTime,
    long? CompleteTime,
    long CreateTime);
