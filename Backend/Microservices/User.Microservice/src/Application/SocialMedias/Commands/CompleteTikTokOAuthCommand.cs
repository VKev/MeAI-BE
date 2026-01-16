using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.TikTok;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CompleteTikTokOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteTikTokOAuthCommandHandler
    : IRequestHandler<CompleteTikTokOAuthCommand, Result<SocialMediaResponse>>
{
    private readonly ITikTokOAuthService _tikTokOAuthService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;

    public CompleteTikTokOAuthCommandHandler(
        ITikTokOAuthService tikTokOAuthService,
        IUnitOfWork unitOfWork)
    {
        _tikTokOAuthService = tikTokOAuthService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        CompleteTikTokOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("TikTok.AuthorizationDenied", request.ErrorDescription ?? request.Error));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("TikTok.MissingCode", "Authorization code is missing"));
        }

        if (!_tikTokOAuthService.TryValidateState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("TikTok.InvalidState", "Invalid or expired state token"));
        }

        var tokenResult = await _tikTokOAuthService.ExchangeCodeForTokenAsync(request.Code, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var tokenResponse = tokenResult.Value;
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            open_id = tokenResponse.OpenId,
            access_token = tokenResponse.AccessToken,
            refresh_token = tokenResponse.RefreshToken,
            expires_at = now.AddSeconds(tokenResponse.ExpiresIn),
            refresh_expires_at = now.AddSeconds(tokenResponse.RefreshExpiresIn),
            scope = tokenResponse.Scope,
            token_type = tokenResponse.TokenType
        }));

        var existingSocialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.UserId == userId &&
                    sm.Type == "tiktok" &&
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
                Type = "tiktok",
                Metadata = metadata,
                CreatedAt = now
            };
            await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);
        }

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
