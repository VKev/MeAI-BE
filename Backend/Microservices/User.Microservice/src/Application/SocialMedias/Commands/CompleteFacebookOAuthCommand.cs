using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Data;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;

    public CompleteFacebookOAuthCommandHandler(IUnitOfWork unitOfWork, IConfiguration configuration)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _configuration = configuration;
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

        if (!TryParseState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.InvalidState", "Invalid or expired state token"));
        }

        var appId = _configuration["Facebook:AppId"];
        var appSecret = _configuration["Facebook:AppSecret"];
        var redirectUri = _configuration["Facebook:RedirectUri"];

        if (string.IsNullOrWhiteSpace(appId) ||
            string.IsNullOrWhiteSpace(appSecret) ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.NotConfigured", "Facebook OAuth is not configured."));
        }

        var tokenResponse = await ExchangeCodeForAccessTokenAsync(
            request.Code,
            appId,
            appSecret,
            redirectUri,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.InvalidCode", "Failed to exchange Facebook code for access token."));
        }

        var debugToken = await ValidateTokenAsync(tokenResponse.AccessToken, appId, appSecret, cancellationToken);
        if (debugToken == null ||
            !debugToken.IsValid ||
            !string.Equals(debugToken.AppId, appId, StringComparison.Ordinal))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.InvalidToken", "Invalid Facebook access token"));
        }

        var profile = await FetchProfileAsync(tokenResponse.AccessToken, cancellationToken);
        if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.ProfileMissing", "Facebook profile is missing"));
        }

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
            ["access_token"] = tokenResponse.AccessToken
        };

        if (tokenResponse.ExpiresIn > 0)
        {
            payload["expires_at"] = now.AddSeconds(tokenResponse.ExpiresIn);
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

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }

    private static bool TryParseState(string state, out Guid userId)
    {
        userId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        try
        {
            var decodedBytes = Convert.FromBase64String(state);
            var decodedState = Encoding.UTF8.GetString(decodedBytes);
            var parts = decodedState.Split('|');

            if (parts.Length >= 1 && Guid.TryParse(parts[0], out userId))
            {
                return true;
            }
        }
        catch
        {
            // Invalid state format
        }

        return false;
    }

    private static async Task<FacebookAccessTokenResponse?> ExchangeCodeForAccessTokenAsync(
        string code,
        string appId,
        string appSecret,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var tokenUrl =
            $"https://graph.facebook.com/v20.0/oauth/access_token?client_id={Uri.EscapeDataString(appId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&client_secret={Uri.EscapeDataString(appSecret)}&code={Uri.EscapeDataString(code)}";

        using var client = new HttpClient();
        using var response = await client.GetAsync(tokenUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<FacebookAccessTokenResponse>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static async Task<MetaDebugToken?> ValidateTokenAsync(
        string accessToken,
        string appId,
        string appSecret,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={appId}|{appSecret}";
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var debug = JsonSerializer.Deserialize<MetaDebugTokenResponse>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return debug?.Data;
    }

    private static async Task<FacebookProfile?> FetchProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://graph.facebook.com/me?fields=id,name,email&access_token={Uri.EscapeDataString(accessToken)}";
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<FacebookProfile>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record FacebookAccessTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record MetaDebugTokenResponse([property: JsonPropertyName("data")] MetaDebugToken? Data);

    private sealed record MetaDebugToken(
        [property: JsonPropertyName("is_valid")] bool IsValid,
        [property: JsonPropertyName("app_id")] string? AppId);

    private sealed record FacebookProfile(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email);
}
