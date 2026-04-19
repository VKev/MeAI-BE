using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.Abstractions.Threads;
using Application.Subscriptions.Services;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CompleteThreadsOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteThreadsOAuthCommandHandler
    : IRequestHandler<CompleteThreadsOAuthCommand, Result<SocialMediaResponse>>
{
    private readonly IThreadsOAuthService _threadsOAuthService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IUserSubscriptionEntitlementService _userSubscriptionEntitlementService;
    private readonly ISocialMediaProfileService _profileService;
    private readonly ILogger<CompleteThreadsOAuthCommandHandler> _logger;

    public CompleteThreadsOAuthCommandHandler(
        IThreadsOAuthService threadsOAuthService,
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService userSubscriptionEntitlementService,
        ISocialMediaProfileService profileService,
        ILogger<CompleteThreadsOAuthCommandHandler> logger)
    {
        _threadsOAuthService = threadsOAuthService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userSubscriptionEntitlementService = userSubscriptionEntitlementService;
        _profileService = profileService;
        _logger = logger;
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        CompleteThreadsOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Threads.AuthorizationDenied", request.ErrorDescription ?? request.Error));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Threads.MissingCode", "Authorization code is missing"));
        }

        if (!_threadsOAuthService.TryValidateState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Threads.InvalidState", "Invalid or expired state token"));
        }

        var tokenResult = await _threadsOAuthService.ExchangeCodeForTokenAsync(request.Code, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var tokenResponse = tokenResult.Value;
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        // Fetch user profile to include in metadata
        var profileResult = await _threadsOAuthService.GetUserProfileAsync(tokenResponse.AccessToken, cancellationToken);
        var profile = profileResult.IsSuccess ? profileResult.Value : null;

        if (profileResult.IsFailure)
        {
            _logger.LogWarning("Threads profile fetch failed during callback: {Error}", profileResult.Error.Description);
        }
        else
        {
            _logger.LogInformation(
                "Threads profile fetched during callback: Id={Id}, Username={Username}, Name={Name}, HasPicture={HasPicture}, Bio={Bio}",
                profile?.Id, profile?.Username, profile?.Name, profile?.ThreadsProfilePictureUrl != null, profile?.ThreadsBiography);
        }

        var userThreadsAccounts = await _socialMediaRepository.GetAll()
            .Where(sm =>
                sm.UserId == userId &&
                sm.Type == "threads" &&
                sm.Metadata != null &&
                !sm.IsDeleted)
            .ToListAsync(cancellationToken);

        var threadsAccountId = !string.IsNullOrWhiteSpace(tokenResponse.UserId)
            ? tokenResponse.UserId
            : profile?.Id;

        SocialMedia? matchedSocialMedia = null;
        if (!string.IsNullOrWhiteSpace(threadsAccountId))
        {
            matchedSocialMedia = userThreadsAccounts.FirstOrDefault(sm =>
                MatchesThreadsAccount(sm.Metadata, threadsAccountId));
        }

        if (matchedSocialMedia == null)
        {
            var entitlementResult = await _userSubscriptionEntitlementService.EnsureSocialAccountLinkAllowedAsync(
                userId,
                cancellationToken);

            if (entitlementResult.IsFailure)
            {
                return Result.Failure<SocialMediaResponse>(entitlementResult.Error);
            }
        }

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            id = profile?.Id ?? tokenResponse.UserId,
            user_id = tokenResponse.UserId,
            access_token = tokenResponse.AccessToken,
            expires_at = now.AddSeconds(tokenResponse.ExpiresIn),
            token_type = tokenResponse.TokenType,
            username = profile?.Username,
            name = profile?.Name,
            profile_picture_url = profile?.ThreadsProfilePictureUrl,
            threads_profile_picture_url = profile?.ThreadsProfilePictureUrl,
            biography = profile?.ThreadsBiography,
            threads_biography = profile?.ThreadsBiography,
            followers_count = profile?.FollowersCount,
            follows_count = profile?.FollowsCount,
            media_count = profile?.MediaCount
        }));

        SocialMedia socialMedia;

        if (matchedSocialMedia != null)
        {
            matchedSocialMedia.Metadata?.Dispose();
            matchedSocialMedia.Metadata = metadata;
            matchedSocialMedia.UpdatedAt = now;
            socialMedia = matchedSocialMedia;
        }
        else
        {
            socialMedia = new SocialMedia
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Type = "threads",
                Metadata = metadata,
                CreatedAt = now
            };
            await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);
        }

        var socialProfile = profile != null
            ? new SocialMediaUserProfile(
                UserId: profile.Id,
                Username: profile.Username,
                DisplayName: profile.Name,
                ProfilePictureUrl: profile.ThreadsProfilePictureUrl,
                Bio: profile.ThreadsBiography,
                FollowerCount: profile.FollowersCount,
                FollowingCount: profile.FollowsCount,
                PostCount: profile.MediaCount,
                PageLikeCount: null)
            : null;

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, socialProfile, includeMetadata: false));
    }

    private static bool MatchesThreadsAccount(JsonDocument? metadata, string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        if (TryGetMetadataValue(metadata, "user_id", out var userId) &&
            string.Equals(userId, accountId, StringComparison.Ordinal))
        {
            return true;
        }

        if (TryGetMetadataValue(metadata, "id", out var id) &&
            string.Equals(id, accountId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetMetadataValue(JsonDocument? metadata, string propertyName, out string value)
    {
        value = string.Empty;

        if (metadata == null)
        {
            return false;
        }

        if (metadata.RootElement.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
