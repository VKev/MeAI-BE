using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Data;
using Application.Abstractions.Instagram;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CompleteInstagramOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteInstagramOAuthCommandHandler
    : IRequestHandler<CompleteInstagramOAuthCommand, Result<SocialMediaResponse>>
{
    private const string InstagramSocialMediaType = "instagram";

    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IInstagramOAuthService _instagramOAuthService;

    public CompleteInstagramOAuthCommandHandler(
        IUnitOfWork unitOfWork,
        IInstagramOAuthService instagramOAuthService)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _instagramOAuthService = instagramOAuthService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        CompleteInstagramOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.AuthorizationDenied", request.ErrorDescription ?? request.Error));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.MissingCode", "Authorization code is missing"));
        }

        if (!_instagramOAuthService.TryValidateState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.InvalidState", "Invalid or expired state token"));
        }

        var tokenResult = await _instagramOAuthService.ExchangeCodeForTokenAsync(
            request.Code,
            cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var accessToken = tokenResult.Value.AccessToken;
        var expiresIn = tokenResult.Value.ExpiresIn;

        var debugResult = await _instagramOAuthService.ValidateTokenAsync(
            accessToken,
            cancellationToken);

        if (debugResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(debugResult.Error);
        }

        var profileResult = await _instagramOAuthService.FetchBusinessProfileAsync(
            accessToken,
            debugResult.Value,
            cancellationToken);

        if (profileResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(profileResult.Error);
        }

        var profile = profileResult.Value.Profile;

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        var resolvedUsername = !string.IsNullOrWhiteSpace(profile.Username)
            ? profile.Username
            : user?.Username;

        var resolvedEmail = user?.Email;

        var payload = new Dictionary<string, object?>
        {
            ["provider"] = InstagramSocialMediaType,
            ["id"] = profile.Id,
            ["username"] = resolvedUsername,
            ["email"] = resolvedEmail,
            ["access_token"] = profileResult.Value.PageAccessToken,
            ["user_access_token"] = accessToken,
            ["token_type"] = tokenResult.Value.TokenType,
            ["user_id"] = profile.Id,
            ["page_id"] = profileResult.Value.PageId,
            ["page_name"] = profileResult.Value.PageName,
            ["instagram_business_account_id"] = profileResult.Value.InstagramAccountId,
            ["instagram_account_type"] = profileResult.Value.InstagramAccountType
        };

        if (expiresIn > 0)
        {
            payload["expires_at"] = now.AddSeconds(expiresIn);
        }

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(payload, MetadataJsonOptions));

        var existingSocialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.UserId == userId &&
                    sm.Type == InstagramSocialMediaType &&
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
                Type = InstagramSocialMediaType,
                Metadata = metadata,
                CreatedAt = now
            };
            await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);
        }

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
