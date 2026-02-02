using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.TikTok;

public interface ITikTokPublishService
{
    Task<Result<TikTokCreatorInfo>> QueryCreatorInfoAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<Result<TikTokPublishResult>> PublishAsync(
        TikTokPublishRequest request,
        CancellationToken cancellationToken);
}

public sealed record TikTokCreatorInfo(
    string CreatorAvatarUrl,
    string CreatorUsername,
    string CreatorNickname,
    IReadOnlyList<string> PrivacyLevelOptions,
    bool CommentDisabled,
    bool DuetDisabled,
    bool StitchDisabled,
    int MaxVideoPostDurationSec);

public sealed record TikTokPublishRequest(
    string AccessToken,
    string OpenId,
    string Caption,
    TikTokPublishMedia Media,
    TikTokCreatorInfo? CreatorInfo = null);

public sealed record TikTokPublishMedia(
    string Url,
    string? ContentType);

public sealed record TikTokPublishResult(
    string OpenId,
    string PublishId,
    string Status);
