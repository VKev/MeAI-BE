using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.TikTok;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record GetTikTokPublishStatusQuery(
    Guid UserId,
    Guid SocialMediaId,
    string PublishId
) : IRequest<Result<TikTokPublishStatusResponse>>;

public sealed class GetTikTokPublishStatusQueryHandler
    : IRequestHandler<GetTikTokPublishStatusQuery, Result<TikTokPublishStatusResponse>>
{
    private readonly ITikTokOAuthService _tikTokService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;

    public GetTikTokPublishStatusQueryHandler(
        ITikTokOAuthService tikTokService,
        IUnitOfWork unitOfWork)
    {
        _tikTokService = tikTokService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<TikTokPublishStatusResponse>> Handle(
        GetTikTokPublishStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Get social media with access token
        var socialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.Id == request.SocialMediaId &&
                    sm.UserId == request.UserId &&
                    sm.Type == "tiktok" &&
                    !sm.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<TikTokPublishStatusResponse>(
                new Error("TikTok.NotFound", "TikTok social media account not found"));
        }

        // Extract access token from metadata
        var accessToken = ExtractAccessToken(socialMedia.Metadata);
        if (string.IsNullOrEmpty(accessToken))
        {
            return Result.Failure<TikTokPublishStatusResponse>(
                new Error("TikTok.InvalidToken", "Access token not found in social media metadata"));
        }

        // Get publish status
        var statusResult = await _tikTokService.GetPublishStatusAsync(
            accessToken,
            request.PublishId,
            cancellationToken);

        if (statusResult.IsFailure)
        {
            return Result.Failure<TikTokPublishStatusResponse>(statusResult.Error);
        }

        return Result.Success(statusResult.Value);
    }

    private static string? ExtractAccessToken(JsonDocument? metadata)
    {
        if (metadata == null)
            return null;

        if (metadata.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            return accessTokenElement.GetString();
        }

        return null;
    }
}
