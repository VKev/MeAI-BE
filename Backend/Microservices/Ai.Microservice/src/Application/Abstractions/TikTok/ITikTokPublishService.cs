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

    /// <summary>
    /// Publishes a photo carousel post using TikTok's
    /// Direct Post photo API endpoint.
    /// </summary>
    Task<Result<TikTokPublishResult>> PublishCarouselAsync(
        TikTokCarouselPublishRequest request,
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
    bool? IsPrivate = null,
    TikTokCreatorInfo? CreatorInfo = null);

/// <summary>
/// Request to publish a TikTok photo carousel post (postType = "posts").
/// </summary>
public sealed record TikTokCarouselPublishRequest(
    string AccessToken,
    string OpenId,
    string Caption,
    /// <summary>Publicly accessible image URLs (1-35 JPEG/WebP images, max 20 MB each).</summary>
    IReadOnlyList<string> ImageUrls,
    bool? IsPrivate = null,
    TikTokCreatorInfo? CreatorInfo = null);

public sealed record TikTokPublishMedia(
    string Url,
    string? ContentType);

public sealed record TikTokPublishResult(
    string OpenId,
    string PublishId,
    string Status);
