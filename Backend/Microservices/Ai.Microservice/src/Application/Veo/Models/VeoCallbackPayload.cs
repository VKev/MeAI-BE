namespace Application.Veo.Models;

public sealed record VeoCallbackPayload(
    int Code,
    string? Msg,
    VeoCallbackData? Data);

public sealed record VeoCallbackData(
    string? TaskId,
    VeoCallbackInfo? Info,
    bool? FallbackFlag);

public sealed record VeoCallbackInfo(
    List<string>? ResultUrls,
    List<string>? OriginUrls,
    string? Resolution);
