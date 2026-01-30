namespace Application.Resources.Models;

public sealed record ResourcePresignResponse(
    Guid Id,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType);
