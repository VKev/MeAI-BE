using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Threads;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record RefreshThreadsTokenCommand(
    Guid SocialMediaId,
    Guid UserId) : IRequest<Result<SocialMediaResponse>>;

public sealed class RefreshThreadsTokenCommandHandler
    : IRequestHandler<RefreshThreadsTokenCommand, Result<SocialMediaResponse>>
{
    private readonly IThreadsOAuthService _threadsOAuthService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;

    public RefreshThreadsTokenCommandHandler(
        IThreadsOAuthService threadsOAuthService,
        IUnitOfWork unitOfWork)
    {
        _threadsOAuthService = threadsOAuthService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        RefreshThreadsTokenCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.Id == request.SocialMediaId &&
                    sm.UserId == request.UserId &&
                    sm.Type == "threads" &&
                    !sm.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Threads.NotFound", "Threads social media connection not found"));
        }

        if (socialMedia.Metadata == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Threads.NoMetadata", "No token metadata found"));
        }

        var accessToken = socialMedia.Metadata.RootElement.TryGetProperty("access_token", out var at)
            ? at.GetString()
            : null;

        if (string.IsNullOrEmpty(accessToken))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Threads.NoAccessToken", "Access token not found"));
        }

        var tokenResult = await _threadsOAuthService.RefreshTokenAsync(accessToken, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var tokenResponse = tokenResult.Value;
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var newMetadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            user_id = tokenResponse.UserId,
            access_token = tokenResponse.AccessToken,
            expires_at = now.AddSeconds(tokenResponse.ExpiresIn),
            token_type = tokenResponse.TokenType
        }));

        socialMedia.Metadata?.Dispose();
        socialMedia.Metadata = newMetadata;
        socialMedia.UpdatedAt = now;

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
