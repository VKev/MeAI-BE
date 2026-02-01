using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.Abstractions.Threads;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private readonly ISocialMediaProfileService _profileService;

    public CompleteThreadsOAuthCommandHandler(
        IThreadsOAuthService threadsOAuthService,
        IUnitOfWork unitOfWork,
        ISocialMediaProfileService profileService)
    {
        _threadsOAuthService = threadsOAuthService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
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

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            user_id = tokenResponse.UserId,
            access_token = tokenResponse.AccessToken,
            expires_at = now.AddSeconds(tokenResponse.ExpiresIn),
            token_type = tokenResponse.TokenType,
            username = profile?.Username,
            name = profile?.Name,
            threads_profile_picture_url = profile?.ThreadsProfilePictureUrl,
            threads_biography = profile?.ThreadsBiography
        }));

        var existingSocialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.UserId == userId &&
                    sm.Type == "threads" &&
                    !sm.IsDeleted,
                cancellationToken);

        SocialMedia socialMedia;

        if (existingSocialMedia != null)
        {
            existingSocialMedia.Metadata?.Dispose();
            existingSocialMedia.Metadata = metadata;
            existingSocialMedia.UpdatedAt = now;
            socialMedia = existingSocialMedia;
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
                FollowerCount: null,
                FollowingCount: null)
            : null;

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, socialProfile));
    }
}
