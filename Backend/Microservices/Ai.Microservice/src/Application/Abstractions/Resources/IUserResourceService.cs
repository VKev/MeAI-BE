using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Common.Resources;

namespace Application.Abstractions.Resources;

public interface IUserResourceService
{
    Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPresignedResourcesAsync(
        Guid userId,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken cancellationToken);

    Task<Result<StorageQuotaCheckResult>> CheckStorageQuotaAsync(
        Guid userId,
        long requestedBytes,
        string purpose,
        int estimatedFileCount,
        CancellationToken cancellationToken,
        Guid? workspaceId = null);

    Task<Result<IReadOnlyList<UserResourceCreatedResult>>> CreateResourcesFromUrlsAsync(
        Guid userId,
        IReadOnlyList<string> urls,
        string? status,
        string? resourceType,
        CancellationToken cancellationToken,
        Guid? workspaceId = null,
        ResourceProvenanceMetadata? provenance = null);

    Task<Result<int>> DeleteResourcesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> resourceIds,
        bool hardDelete,
        CancellationToken cancellationToken);

    Task<Result<int>> BackfillResourceProvenanceAsync(
        IReadOnlyList<ResourceProvenanceBackfillRequest> items,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyDictionary<Guid, PublicUserProfileResult>>> GetPublicUserProfilesByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed record UserResourcePresignResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    string? OriginKind = null,
    string? OriginSourceUrl = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);

public sealed record UserResourceCreatedResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    string? OriginKind = null,
    string? OriginSourceUrl = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);

public sealed record StorageQuotaCheckResult(
    bool Allowed,
    long? QuotaBytes,
    long UsedBytes,
    long ReservedBytes,
    long AvailableBytes,
    long? MaxUploadFileBytes,
    long? SystemStorageQuotaBytes,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ResourceProvenanceBackfillRequest(
    Guid ResourceId,
    string? OriginKind,
    string? OriginSourceUrl,
    Guid? OriginChatSessionId,
    Guid? OriginChatId);

public sealed record PublicUserProfileResult(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl);
