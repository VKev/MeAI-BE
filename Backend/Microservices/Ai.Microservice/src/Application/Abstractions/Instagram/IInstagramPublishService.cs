using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Instagram;

public interface IInstagramPublishService
{
    Task<Result<InstagramPublishResult>> PublishAsync(
        InstagramPublishRequest request,
        CancellationToken cancellationToken);

    Task<Result<bool>> DeleteAsync(
        InstagramDeleteRequest request,
        CancellationToken cancellationToken);
}

public sealed record InstagramDeleteRequest(
    string MediaId,
    string AccessToken);

public sealed record InstagramPublishRequest(
    string AccessToken,
    string InstagramUserId,
    string Caption,
    InstagramPublishMedia Media);

public sealed record InstagramPublishMedia(
    string Url,
    string? ContentType);

public sealed record InstagramPublishResult(
    string InstagramUserId,
    string PostId);
