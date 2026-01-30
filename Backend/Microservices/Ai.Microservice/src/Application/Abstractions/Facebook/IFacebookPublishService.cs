using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Facebook;

public interface IFacebookPublishService
{
    Task<Result<IReadOnlyList<FacebookPublishResult>>> PublishAsync(
        FacebookPublishRequest request,
        CancellationToken cancellationToken);
}

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
