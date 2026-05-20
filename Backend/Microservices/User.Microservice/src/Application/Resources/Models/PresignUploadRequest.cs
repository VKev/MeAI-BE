namespace Application.Resources.Models;

public sealed record PresignUploadRequest(
    string FileName,
    string ContentType,
    long ContentLength,
    string? ResourceType = null,
    Guid? WorkspaceId = null);
