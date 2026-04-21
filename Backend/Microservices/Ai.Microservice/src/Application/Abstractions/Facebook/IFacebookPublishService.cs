using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Facebook;

public interface IFacebookPublishService
{
    Task<Result<IReadOnlyList<FacebookPublishResult>>> PublishAsync(
        FacebookPublishRequest request,
        CancellationToken cancellationToken);

    Task<Result<bool>> DeleteAsync(
        FacebookDeleteRequest request,
        CancellationToken cancellationToken);

    Task<Result<bool>> UpdateAsync(
        FacebookUpdateRequest request,
        CancellationToken cancellationToken);
}

public sealed record FacebookDeleteRequest(
    string ExternalPostId,
    string PageAccessToken);

public sealed record FacebookUpdateRequest(
    string ExternalPostId,
    string PageAccessToken,
    string Message);

public sealed record FacebookPublishRequest(
    string UserAccessToken,
    string? PageId,
    string? PageAccessToken,
    string Message,
    IReadOnlyList<FacebookPublishMedia> Media);

public sealed record FacebookPublishMedia(
    string Url,
    string? ContentType);

public sealed record FacebookPublishResult(
    string PageId,
    string PostId);
