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

public sealed record CompleteInstagramOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteInstagramOAuthCommandHandler
    : IRequestHandler<CompleteInstagramOAuthCommand, Result<SocialMediaResponse>>
{
    private const string InstagramSocialMediaType = "instagram";
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v20.0";
    private const string OAuthTokenEndpoint = "https://graph.facebook.com/v20.0/oauth/access_token";

    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IConfiguration _configuration;

    public CompleteInstagramOAuthCommandHandler(IUnitOfWork unitOfWork, IConfiguration configuration)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _configuration = configuration;
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

        if (!TryParseState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.InvalidState", "Invalid or expired state token"));
        }

        var appId = _configuration["Instagram:AppId"] ?? _configuration["Facebook:AppId"];
        var appSecret = _configuration["Instagram:AppSecret"] ?? _configuration["Facebook:AppSecret"];
        var redirectUri = _configuration["Instagram:RedirectUri"] ?? _configuration["Facebook:RedirectUri"];

        if (string.IsNullOrWhiteSpace(appId) ||
            string.IsNullOrWhiteSpace(appSecret) ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.NotConfigured", "Instagram OAuth is not configured."));
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
                new Error("Instagram.InvalidCode", "Failed to exchange Instagram code for access token."));
        }

        var accessToken = tokenResponse.AccessToken!;
        var expiresIn = tokenResponse.ExpiresIn;

        var debugToken = await ValidateTokenAsync(accessToken, appId, appSecret, cancellationToken);
        if (debugToken == null || !debugToken.IsValid)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.InvalidToken", "Invalid Instagram access token."));
        }

        if (!string.IsNullOrWhiteSpace(debugToken.AppId) &&
            !string.Equals(debugToken.AppId, appId, StringComparison.Ordinal))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.AppMismatch", "Instagram token does not belong to this app."));
        }

        var profileLookup = await FetchInstagramBusinessProfileAsync(accessToken, cancellationToken);
        if (profileLookup.Profile == null ||
            profileLookup.Profile.Profile == null ||
            string.IsNullOrWhiteSpace(profileLookup.Profile.Profile.Id))
        {
            if (!string.IsNullOrWhiteSpace(profileLookup.ErrorMessage))
            {
                return Result.Failure<SocialMediaResponse>(
                    new Error("Instagram.GraphApiError", profileLookup.ErrorMessage));
            }

            if (profileLookup.PageCount == 0)
            {
                var missingPermissions = GetMissingPermissions(debugToken);
                if (missingPermissions.Count > 0)
                {
                    return Result.Failure<SocialMediaResponse>(
                        new Error("Instagram.MissingPermissions",
                            $"Missing required permissions: {string.Join(", ", missingPermissions)}."));
                }

                return Result.Failure<SocialMediaResponse>(
                    new Error("Instagram.NoPages", "No Facebook Pages were returned for this Facebook user."));
            }

            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.NoBusinessAccount",
                    "No Instagram business or creator account found for this Facebook user."));
        }

        var profileResult = profileLookup.Profile;
        var profile = profileResult.Profile;

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
            ["access_token"] = profileResult.PageAccessToken,
            ["user_access_token"] = accessToken,
            ["token_type"] = tokenResponse.TokenType,
            ["user_id"] = profile.Id,
            ["page_id"] = profileResult.PageId,
            ["page_name"] = profileResult.PageName,
            ["instagram_business_account_id"] = profileResult.InstagramAccountId,
            ["instagram_account_type"] = profileResult.InstagramAccountType
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
        var url =
            $"{OAuthTokenEndpoint}?client_id={Uri.EscapeDataString(appId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&client_secret={Uri.EscapeDataString(appSecret)}&code={Uri.EscapeDataString(code)}";

        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<FacebookAccessTokenResponse>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static async Task<InstagramGraphProfileLookupResult> FetchInstagramBusinessProfileAsync(
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var (pages, pagesError) = await FetchFacebookPagesAsync(userAccessToken, cancellationToken);
        var lastError = pagesError;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id))
            {
                continue;
            }

            var pageAccessToken = page.AccessToken;
            if (string.IsNullOrWhiteSpace(pageAccessToken))
            {
                var (token, tokenError) = await FetchPageAccessTokenAsync(
                    page.Id,
                    userAccessToken,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(tokenError) && string.IsNullOrWhiteSpace(lastError))
                {
                    lastError = tokenError;
                }

                pageAccessToken = token;
            }

            if (string.IsNullOrWhiteSpace(pageAccessToken))
            {
                continue;
            }

            var instagramAccountId = page.InstagramBusinessAccount?.Id;
            var instagramAccountType = !string.IsNullOrWhiteSpace(instagramAccountId) ? "business" : null;

            if (string.IsNullOrWhiteSpace(instagramAccountId))
            {
                instagramAccountId = page.ConnectedInstagramAccount?.Id;
                if (!string.IsNullOrWhiteSpace(instagramAccountId))
                {
                    instagramAccountType = "creator";
                }
            }

            if (string.IsNullOrWhiteSpace(instagramAccountId))
            {
                var (instagramAccount, instagramError) = await FetchInstagramAccountAsync(
                    page.Id,
                    pageAccessToken,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(instagramError) && string.IsNullOrWhiteSpace(lastError))
                {
                    lastError = instagramError;
                }

                instagramAccountId = instagramAccount?.Id;
                instagramAccountType = instagramAccount?.Type;
            }

            if (string.IsNullOrWhiteSpace(instagramAccountId))
            {
                continue;
            }

            var (profile, profileError) = await FetchInstagramProfileAsync(
                instagramAccountId,
                pageAccessToken,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(profileError) && string.IsNullOrWhiteSpace(lastError))
            {
                lastError = profileError;
            }

            if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
            {
                continue;
            }

            return new InstagramGraphProfileLookupResult(
                new InstagramGraphProfileResult(
                    profile,
                    page.Id,
                    page.Name,
                    pageAccessToken,
                    instagramAccountId!,
                    instagramAccountType),
                lastError,
                pages.Count);
        }

        return new InstagramGraphProfileLookupResult(null, lastError, pages.Count);
    }

    private static async Task<(List<FacebookPage> Pages, string? ErrorMessage)> FetchFacebookPagesAsync(
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/me/accounts?fields=id,name,access_token,tasks,instagram_business_account,connected_instagram_account&access_token={Uri.EscapeDataString(userAccessToken)}";

        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await ReadGraphApiErrorAsync(response, cancellationToken);
            return (new List<FacebookPage>(), errorMessage);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<FacebookPagesResponse>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return (result?.Data ?? new List<FacebookPage>(), null);
    }

    private static async Task<(string? AccessToken, string? ErrorMessage)> FetchPageAccessTokenAsync(
        string pageId,
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}?fields=access_token&access_token={Uri.EscapeDataString(userAccessToken)}";

        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await ReadGraphApiErrorAsync(response, cancellationToken);
            return (null, errorMessage);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<FacebookPageAccessTokenResponse>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return (result?.AccessToken, null);
    }

    private static async Task<(InstagramAccountReference? Account, string? ErrorMessage)> FetchInstagramAccountAsync(
        string pageId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}?fields=instagram_business_account,connected_instagram_account&access_token={Uri.EscapeDataString(pageAccessToken)}";

        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await ReadGraphApiErrorAsync(response, cancellationToken);
            return (null, errorMessage);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<FacebookPageInstagramAccountResponse>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (!string.IsNullOrWhiteSpace(result?.InstagramBusinessAccount?.Id))
        {
            return (new InstagramAccountReference(result.InstagramBusinessAccount.Id!, "business"), null);
        }

        if (!string.IsNullOrWhiteSpace(result?.ConnectedInstagramAccount?.Id))
        {
            return (new InstagramAccountReference(result.ConnectedInstagramAccount.Id!, "creator"), null);
        }

        return (null, null);
    }

    private static async Task<(InstagramProfile? Profile, string? ErrorMessage)> FetchInstagramProfileAsync(
        string instagramAccountId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(instagramAccountId)}?fields=id,username&access_token={Uri.EscapeDataString(pageAccessToken)}";

        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await ReadGraphApiErrorAsync(response, cancellationToken);
            return (null, errorMessage);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var profile = JsonSerializer.Deserialize<InstagramProfile>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return (profile, null);
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

    private static List<string> GetMissingPermissions(MetaDebugToken debugToken)
    {
        var grantedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (debugToken.Scopes != null)
        {
            foreach (var scope in debugToken.Scopes)
            {
                if (!string.IsNullOrWhiteSpace(scope))
                {
                    grantedScopes.Add(scope);
                }
            }
        }

        if (debugToken.GranularScopes != null)
        {
            foreach (var scope in debugToken.GranularScopes
                         .Select(item => item.Scope)
                         .Where(scope => !string.IsNullOrWhiteSpace(scope)))
            {
                grantedScopes.Add(scope!);
            }
        }

        var required = new[] { "pages_show_list", "instagram_basic" };
        var missing = new List<string>();

        foreach (var scope in required)
        {
            if (!grantedScopes.Contains(scope))
            {
                missing.Add(scope);
            }
        }

        return missing;
    }

    private static async Task<string?> ReadGraphApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return response.ReasonPhrase ?? "Graph API request failed.";
            }

            var errorResponse = JsonSerializer.Deserialize<GraphApiErrorResponse>(
                payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return errorResponse?.Error == null
                ? response.ReasonPhrase ?? "Graph API request failed."
                : FormatGraphApiErrorMessage(errorResponse.Error);
        }
        catch
        {
            return response.ReasonPhrase ?? "Graph API request failed.";
        }
    }

    private static string FormatGraphApiErrorMessage(GraphApiError error)
    {
        var mainMessage = !string.IsNullOrWhiteSpace(error.ErrorUserMessage)
            ? error.ErrorUserMessage
            : error.Message;

        if (!string.IsNullOrWhiteSpace(error.ErrorUserTitle))
        {
            mainMessage = string.IsNullOrWhiteSpace(mainMessage)
                ? error.ErrorUserTitle
                : $"{error.ErrorUserTitle}: {mainMessage}";
        }

        var details = new List<string>();

        if (error.Code.HasValue)
        {
            details.Add($"code={error.Code.Value}");
        }

        if (error.ErrorSubcode.HasValue)
        {
            details.Add($"subcode={error.ErrorSubcode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(error.Type))
        {
            details.Add($"type={error.Type}");
        }

        if (!string.IsNullOrWhiteSpace(error.FbTraceId))
        {
            details.Add($"trace={error.FbTraceId}");
        }

        if (details.Count == 0)
        {
            return string.IsNullOrWhiteSpace(mainMessage)
                ? "Graph API request failed."
                : mainMessage;
        }

        return string.IsNullOrWhiteSpace(mainMessage)
            ? $"Graph API request failed ({string.Join(", ", details)})."
            : $"{mainMessage} ({string.Join(", ", details)}).";
    }

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record FacebookAccessTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType);

    private sealed record FacebookPagesResponse([property: JsonPropertyName("data")] List<FacebookPage>? Data);

    private sealed record FacebookPage(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("instagram_business_account")] InstagramBusinessAccount? InstagramBusinessAccount,
        [property: JsonPropertyName("connected_instagram_account")] InstagramBusinessAccount? ConnectedInstagramAccount,
        [property: JsonPropertyName("tasks")] List<string>? Tasks);

    private sealed record FacebookPageAccessTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record FacebookPageInstagramAccountResponse(
        [property: JsonPropertyName("instagram_business_account")] InstagramBusinessAccount? InstagramBusinessAccount,
        [property: JsonPropertyName("connected_instagram_account")] InstagramBusinessAccount? ConnectedInstagramAccount);

    private sealed record InstagramBusinessAccount([property: JsonPropertyName("id")] string? Id);

    private sealed record InstagramProfile(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("username")] string? Username);

    private sealed record GraphApiErrorResponse([property: JsonPropertyName("error")] GraphApiError? Error);

    private sealed record GraphApiError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
        [property: JsonPropertyName("error_user_title")] string? ErrorUserTitle,
        [property: JsonPropertyName("error_user_msg")] string? ErrorUserMessage,
        [property: JsonPropertyName("fbtrace_id")] string? FbTraceId);

    private sealed record MetaDebugTokenResponse([property: JsonPropertyName("data")] MetaDebugToken? Data);

    private sealed record MetaDebugToken(
        [property: JsonPropertyName("is_valid")] bool IsValid,
        [property: JsonPropertyName("app_id")] string? AppId,
        [property: JsonPropertyName("scopes")] List<string>? Scopes,
        [property: JsonPropertyName("granular_scopes")] List<MetaGranularScope>? GranularScopes);

    private sealed record MetaGranularScope(
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("target_ids")] List<string>? TargetIds);

    private sealed record InstagramGraphProfileResult(
        InstagramProfile Profile,
        string PageId,
        string? PageName,
        string PageAccessToken,
        string InstagramAccountId,
        string? InstagramAccountType);

    private sealed record InstagramGraphProfileLookupResult(
        InstagramGraphProfileResult? Profile,
        string? ErrorMessage,
        int PageCount);

    private sealed record InstagramAccountReference(string Id, string Type);
}
