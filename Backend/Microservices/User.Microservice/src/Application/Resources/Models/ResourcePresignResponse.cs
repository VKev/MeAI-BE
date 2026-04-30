namespace Application.Resources.Models;

public sealed record ResourcePresignResponse(
    Guid Id,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    string? OriginKind = null,
    string? OriginSourceUrl = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);
