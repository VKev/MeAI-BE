namespace Application.Resources.Models;

public sealed record PresignedUploadResponse(
    Guid ResourceId,
    string UploadUrl,
    string StorageKey,
    string Method,
    IReadOnlyDictionary<string, string> Headers);
