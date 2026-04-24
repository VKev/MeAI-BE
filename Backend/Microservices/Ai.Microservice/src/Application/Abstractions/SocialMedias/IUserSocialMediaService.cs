using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.SocialMedias;

public interface IUserSocialMediaService
{
    Task<Result<IReadOnlyList<UserSocialMediaSummaryResult>>> GetSocialMediasByUserAsync(
        Guid userId,
        string? platform,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserSocialMediaSummaryResult>>> GetWorkspaceSocialMediasAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserSocialMediaResult>>> GetSocialMediasAsync(
        Guid userId,
        IReadOnlyList<Guid> socialMediaIds,
        CancellationToken cancellationToken);
}

public sealed record UserSocialMediaResult(
    Guid SocialMediaId,
    string Type,
    string? MetadataJson);

public sealed record UserSocialMediaSummaryResult(
    Guid SocialMediaId,
    string Type,
    string? Username,
    string? DisplayName,
    string? ProfilePictureUrl,
    string? PageId,
    string? PageName,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
