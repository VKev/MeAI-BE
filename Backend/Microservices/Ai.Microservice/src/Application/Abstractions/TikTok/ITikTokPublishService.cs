using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.TikTok;

public interface ITikTokPublishService
{
    Task<Result<TikTokPublishResult>> PublishAsync(
        TikTokPublishRequest request,
        CancellationToken cancellationToken);
}

public sealed record TikTokPublishRequest(
    string AccessToken,
    string OpenId,
    string Caption,
    TikTokPublishMedia Media);

public sealed record TikTokPublishMedia(
    string Url,
    string? ContentType);

public sealed record TikTokPublishResult(
    string OpenId,
    string PublishId,
    string Status);
