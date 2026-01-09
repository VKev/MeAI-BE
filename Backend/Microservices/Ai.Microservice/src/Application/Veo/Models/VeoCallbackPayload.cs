namespace Application.Veo.Models;

public sealed record VeoCallbackPayload(
    int Code,
    string? Msg,
    VeoCallbackData? Data,
    bool? FallbackFlag);

public sealed record VeoCallbackData(
    string? TaskId,
    VeoCallbackInfo? Info);

public sealed record VeoCallbackInfo(
    string? ResultUrls,
    string? OriginUrls,
    string? Resolution);
