namespace Application.Abstractions;

public interface IVeoVideoService
{
    Task<VeoGenerateResult> GenerateVideoAsync(VeoGenerateRequest request, CancellationToken cancellationToken = default);
    Task<VeoExtendResult> ExtendVideoAsync(VeoExtendRequest request, CancellationToken cancellationToken = default);
    Task<VeoRecordInfoResult> GetVideoDetailsAsync(string taskId, CancellationToken cancellationToken = default);
    Task<Veo1080PResult> Get1080PVideoAsync(string taskId, int index = 0, CancellationToken cancellationToken = default);
}

public sealed record VeoGenerateRequest(
    string Prompt,
    List<string>? ImageUrls = null,
    string Model = "veo3_fast",
    string? GenerationType = null,
    string AspectRatio = "16:9",
    int? Seeds = null,
    bool EnableTranslation = true,
    string? Watermark = null,
    Guid? CorrelationId = null);

public sealed record VeoGenerateResult(
    bool Success,
    int Code,
    string Message,
    string? TaskId);

public sealed record VeoExtendRequest(
    string TaskId,
    string Prompt,
    int? Seeds = null,
    string? Watermark = null,
    Guid? CorrelationId = null);

public sealed record VeoExtendResult(
    bool Success,
    int Code,
    string Message,
    string? TaskId);

public sealed record VeoRecordInfoResult(
    bool Success,
    int Code,
    string Message,
    VeoRecordInfo? Data);

public sealed record VeoRecordInfo(
    string TaskId,
    string? ParamJson,
    DateTime? CompleteTime,
    VeoRecordResponse? Response,
    int SuccessFlag,
    int? ErrorCode,
    string? ErrorMessage,
    DateTime? CreateTime,
    bool FallbackFlag);

public sealed record VeoRecordResponse(
    string TaskId,
    List<string>? ResultUrls,
    List<string>? OriginUrls,
    string? Resolution);

public sealed record Veo1080PResult(
    bool Success,
    int Code,
    string Message,
    string? ResultUrl);

