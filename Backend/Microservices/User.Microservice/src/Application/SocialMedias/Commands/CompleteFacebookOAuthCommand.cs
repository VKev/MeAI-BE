using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Facebook;
using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CompleteFacebookOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteFacebookOAuthCommandHandler
    : IRequestHandler<CompleteFacebookOAuthCommand, Result<SocialMediaResponse>>
{
    private const string FacebookSocialMediaType = "facebook";

    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IFacebookOAuthService _facebookOAuthService;
    private readonly ISocialMediaProfileService _profileService;

    public CompleteFacebookOAuthCommandHandler(
        IUnitOfWork unitOfWork,
        IFacebookOAuthService facebookOAuthService,
        ISocialMediaProfileService profileService)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _facebookOAuthService = facebookOAuthService;
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        CompleteFacebookOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.AuthorizationDenied", request.ErrorDescription ?? request.Error));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.MissingCode", "Authorization code is missing"));
        }

        if (!_facebookOAuthService.TryValidateState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.InvalidState", "Invalid or expired state token"));
        }

        var tokenResult = await _facebookOAuthService.ExchangeCodeForTokenAsync(
            request.Code,
            cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var debugResult = await _facebookOAuthService.ValidateTokenAsync(
            tokenResult.Value.AccessToken,
            cancellationToken);

        if (debugResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(debugResult.Error);
        }

        var profileResult = await _facebookOAuthService.FetchProfileAsync(
            tokenResult.Value.AccessToken,
            cancellationToken);

        if (profileResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(profileResult.Error);
        }

        var profile = profileResult.Value;

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("User.NotFound", "User not found"));
        }

        var resolvedEmail = user.Email;
        var resolvedName = user.FullName ?? user.Username;
        var userUpdated = false;

        if (!string.IsNullOrWhiteSpace(profile.Name))
        {
            var normalizedName = profile.Name.Trim();
            if (!string.Equals(user.FullName, normalizedName, StringComparison.Ordinal))
            {
                user.FullName = normalizedName;
                userUpdated = true;
            }

            resolvedName = normalizedName;
        }

        if (!string.IsNullOrWhiteSpace(profile.Email))
        {
            var normalizedEmail = NormalizeEmail(profile.Email);
            if (!string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                var emailExists = await _userRepository.GetAll()
                    .AsNoTracking()
                    .AnyAsync(
                        u => u.Email.ToLower() == normalizedEmail && u.Id != user.Id,
                        cancellationToken);

                if (emailExists)
                {
                    return Result.Failure<SocialMediaResponse>(
                        new Error("User.EmailTaken", "Email is already registered"));
                }

                user.Email = normalizedEmail;
                userUpdated = true;
            }

            resolvedEmail = normalizedEmail;
        }

        if (userUpdated)
        {
            user.UpdatedAt = now;
            _userRepository.Update(user);
        }

        var payload = new Dictionary<string, object?>
        {
            ["provider"] = FacebookSocialMediaType,
            ["id"] = profile.Id,
            ["name"] = resolvedName,
            ["email"] = resolvedEmail,
            ["access_token"] = tokenResult.Value.AccessToken
        };

        if (tokenResult.Value.ExpiresIn > 0)
        {
            payload["expires_at"] = now.AddSeconds(tokenResult.Value.ExpiresIn);
        }

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(payload, MetadataJsonOptions));

        var existingSocialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.UserId == userId &&
                    sm.Type == FacebookSocialMediaType &&
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
                Type = FacebookSocialMediaType,
                Metadata = metadata,
                CreatedAt = now
            };
            await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);
        }

        var socialProfileResult = await _profileService.GetUserProfileAsync(
            socialMedia.Type,
            socialMedia.Metadata,
            cancellationToken);

        var socialProfile = socialProfileResult.IsSuccess ? socialProfileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, socialProfile));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

}
