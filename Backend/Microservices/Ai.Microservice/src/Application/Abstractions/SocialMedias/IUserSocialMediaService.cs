using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.SocialMedias;

public interface IUserSocialMediaService
{
    Task<Result<IReadOnlyList<UserSocialMediaResult>>> GetSocialMediasAsync(
        Guid userId,
        IReadOnlyList<Guid> socialMediaIds,
        CancellationToken cancellationToken);
}

public sealed record UserSocialMediaResult(
    Guid SocialMediaId,
    string Type,
    string? MetadataJson);
