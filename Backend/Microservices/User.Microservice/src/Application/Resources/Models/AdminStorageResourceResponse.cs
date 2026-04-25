namespace Application.Resources.Models;

public sealed record AdminStorageResourceResponse(
    Guid Id,
    Guid UserId,
    Guid? WorkspaceId,
    string Link,
    string? PresignedUrl,
    string? Status,
    string? ResourceType,
    string? ContentType,
    long? SizeBytes,
    string? StorageBucket,
    string? StorageRegion,
    string? StorageNamespace,
    string? StorageKey,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt,
    DateTime? ExpiresAt,
    DateTime? DeletedFromStorageAt);
