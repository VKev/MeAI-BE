namespace Application.Kie.Models;

public sealed record KieCallbackPayload(
    int Code,
    string? Msg,
    KieCallbackData? Data);

public sealed record KieCallbackData(
    string? TaskId,
    string? State,
    string? ResultJson,
    string? FailCode,
    string? FailMsg,
    long? CompleteTime);

public sealed record KieResultJson(
    List<string>? ResultUrls);
