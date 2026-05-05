using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Facebook;
using Application.Abstractions.Meta;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Facebook;

public sealed class FacebookOAuthService : IFacebookOAuthService
{
    private const string AuthorizationBaseUrl = "https://www.facebook.com/v24.0/dialog/oauth";
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v24.0";
    private const string OAuthTokenEndpoint = "https://graph.facebook.com/v24.0/oauth/access_token";
    private const string DefaultScopes = "email,public_profile";
    private const string ProfileFields = "id,name,email,picture.type(large)";
    private const string PageFields =
        "id,name,access_token,fan_count,followers_count,posts.limit(0).summary(true)," +
        "username,about,description,category,bio,website,emails,phone," +
        "location{street,city,country,zip},single_line_address,picture.type(large)";

    private readonly string _redirectUri;
    private readonly string _scopes;
    private readonly HttpClient _httpClient;
    private readonly IApiCredentialProvider _credentialProvider;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FacebookOAuthService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IApiCredentialProvider credentialProvider)
    {
        _redirectUri = configuration["Facebook:RedirectUri"]
                       ?? throw new InvalidOperationException("Facebook:RedirectUri is not configured");
        var configuredScopes = configuration["Facebook:Scopes"];
        _scopes = string.IsNullOrWhiteSpace(configuredScopes)
            ? DefaultScopes
            : configuredScopes;
        _httpClient = httpClientFactory.CreateClient("Facebook");
        _credentialProvider = credentialProvider;
    }

    public (string AuthorizationUrl, string State) GenerateAuthorizationUrl(Guid userId, string? scopes)
    {
        var resolvedScopes = string.IsNullOrWhiteSpace(scopes)
            ? _scopes
            : scopes;

        var state = BuildState(userId);

        var appId = _credentialProvider.GetRequiredValue("Facebook", "AppId");
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = _redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["scope"] = resolvedScopes
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return ($"{AuthorizationBaseUrl}?{queryString}", state);
    }

    public async Task<Result<FacebookAccessTokenResponse>> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            var appId = _credentialProvider.GetRequiredValue("Facebook", "AppId");
            var appSecret = _credentialProvider.GetRequiredValue("Facebook", "AppSecret");
            var url =
                $"{OAuthTokenEndpoint}?client_id={Uri.EscapeDataString(appId)}&redirect_uri={Uri.EscapeDataString(_redirectUri)}&client_secret={Uri.EscapeDataString(appSecret)}&code={Uri.EscapeDataString(code)}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ReadGraphApiError(payload) ?? "Failed to exchange Facebook code for access token.";
                return Result.Failure<FacebookAccessTokenResponse>(
                    new Error("Facebook.InvalidCode", errorMessage));
            }

            var token = JsonSerializer.Deserialize<FacebookAccessTokenResponse>(payload, JsonOptions);
            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return Result.Failure<FacebookAccessTokenResponse>(
                    new Error("Facebook.InvalidCode", "Failed to exchange Facebook code for access token."));
            }

            return Result.Success(token);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<FacebookAccessTokenResponse>(
                new Error("Facebook.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<FacebookAccessTokenResponse>(
                new Error("Facebook.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<MetaDebugToken>> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var appId = _credentialProvider.GetRequiredValue("Facebook", "AppId");
            var appSecret = _credentialProvider.GetRequiredValue("Facebook", "AppSecret");
            var url =
                $"{GraphApiBaseUrl}/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={appId}|{appSecret}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ReadGraphApiError(payload) ?? "Invalid Facebook access token";
                return Result.Failure<MetaDebugToken>(new Error("Facebook.InvalidToken", errorMessage));
            }

            var debugResponse = JsonSerializer.Deserialize<MetaDebugTokenResponse>(payload, JsonOptions);
            var debugToken = debugResponse?.Data;

            if (debugToken == null || !debugToken.IsValid)
            {
                return Result.Failure<MetaDebugToken>(
                    new Error("Facebook.InvalidToken", "Invalid Facebook access token"));
            }

            if (!string.IsNullOrWhiteSpace(debugToken.AppId) &&
                !string.Equals(debugToken.AppId, appId, StringComparison.Ordinal))
            {
                return Result.Failure<MetaDebugToken>(
                    new Error("Facebook.InvalidToken", "Invalid Facebook access token"));
            }

            return Result.Success(debugToken);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<MetaDebugToken>(
                new Error("Facebook.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<MetaDebugToken>(
                new Error("Facebook.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<FacebookProfileResponse>> FetchProfileAsync(
        string accessToken,
        CancellationToken cancellationToken,
        string? preferredPageId = null)
    {
        try
        {
            var url =
                $"{GraphApiBaseUrl}/me?fields={Uri.EscapeDataString(ProfileFields)}&access_token={Uri.EscapeDataString(accessToken)}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ReadGraphApiError(payload) ?? "Failed to fetch Facebook profile";
                return Result.Failure<FacebookProfileResponse>(
                    new Error("Facebook.GraphApiError", errorMessage));
            }

            var profile = JsonSerializer.Deserialize<FacebookProfileResponse>(payload, JsonOptions);

            if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
            {
                return Result.Failure<FacebookProfileResponse>(
                    new Error("Facebook.ProfileMissing", "Facebook profile is missing"));
            }

            var pages = await FetchPagesAsync(accessToken, cancellationToken);
            profile.Pages = pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Id))
                .Select(page => new FacebookPageProfile(
                    page.Id!,
                    page.Name,
                    page.AccessToken,
                    page.FanCount,
                    page.FollowersCount,
                    page.Posts?.Summary?.TotalCount))
                .ToList();

            var selectedPage = SelectPage(pages, preferredPageId);
            if (selectedPage != null)
            {
                profile.PageId = selectedPage.Id;
                profile.PageName = selectedPage.Name;
                profile.PageAccessToken = selectedPage.AccessToken;
                profile.PageLikeCount = selectedPage.FanCount;
                profile.PageFollowerCount = selectedPage.FollowersCount;
                profile.PagePostCount = selectedPage.Posts?.Summary?.TotalCount;

                // Profile-fields extension — populated when the page has set them.
                profile.PageUsername = selectedPage.Username;
                profile.PageAbout = selectedPage.About;
                profile.PageDescription = selectedPage.Description;
                profile.PageCategory = selectedPage.Category;
                profile.PageBio = selectedPage.Bio;
                profile.PageWebsite = selectedPage.Website;
                profile.PagePhone = selectedPage.Phone;
                profile.PageEmail = selectedPage.Emails is { Length: > 0 }
                    ? string.Join(", ", selectedPage.Emails.Where(e => !string.IsNullOrWhiteSpace(e)))
                    : null;
                profile.PageLocation = selectedPage.SingleLineAddress
                                       ?? FormatLocation(selectedPage.Location);
                profile.PageProfilePictureUrl = selectedPage.Picture?.Data?.Url;
            }

            return Result.Success(profile);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<FacebookProfileResponse>(
                new Error("Facebook.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<FacebookProfileResponse>(
                new Error("Facebook.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public bool TryValidateState(string state, out Guid userId)
    {
        userId = Guid.Empty;

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

    private static string BuildState(Guid userId)
    {
        var stateData = $"{userId}|{GenerateRandomString(16)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(stateData));
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)[..length]
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
    }

    private static string? ReadGraphApiError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var errorResponse = JsonSerializer.Deserialize<GraphApiErrorResponse>(payload, JsonOptions);
            if (errorResponse?.Error == null)
            {
                return null;
            }

            return FormatGraphApiErrorMessage(errorResponse.Error);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<FacebookPageDataResponse>> FetchPagesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/me/accounts?fields={Uri.EscapeDataString(PageFields)}&access_token={Uri.EscapeDataString(accessToken)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var pages = JsonSerializer.Deserialize<FacebookPagesResponse>(payload, JsonOptions);
        return pages?.Data ?? [];
    }

    private static FacebookPageDataResponse? SelectPage(
        IReadOnlyList<FacebookPageDataResponse> pages,
        string? preferredPageId)
    {
        if (pages.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredPageId))
        {
            var preferredPage = pages.FirstOrDefault(page =>
                string.Equals(page.Id, preferredPageId, StringComparison.Ordinal));

            if (preferredPage != null)
            {
                return preferredPage;
            }
        }

        return pages.FirstOrDefault(page => !string.IsNullOrWhiteSpace(page.Id));
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

    private sealed record GraphApiErrorResponse([property: JsonPropertyName("error")] GraphApiError? Error);

    private sealed record GraphApiError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
        [property: JsonPropertyName("error_user_title")] string? ErrorUserTitle,
        [property: JsonPropertyName("error_user_msg")] string? ErrorUserMessage,
        [property: JsonPropertyName("fbtrace_id")] string? FbTraceId);

    private static string? FormatLocation(FacebookPageLocationResponse? loc)
    {
        if (loc is null) return null;
        var parts = new[] { loc.Street, loc.City, loc.Zip, loc.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private sealed record FacebookPagesResponse(
        [property: JsonPropertyName("data")] List<FacebookPageDataResponse>? Data);

    private sealed record FacebookPageDataResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("fan_count")] int? FanCount,
        [property: JsonPropertyName("followers_count")] int? FollowersCount,
        [property: JsonPropertyName("posts")] FacebookPagePostsResponse? Posts,
        // Profile-fields extension. All optional — FB returns null/missing when the
        // page hasn't filled them in.
        [property: JsonPropertyName("username")] string? Username = null,
        [property: JsonPropertyName("about")] string? About = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("category")] string? Category = null,
        [property: JsonPropertyName("bio")] string? Bio = null,
        [property: JsonPropertyName("website")] string? Website = null,
        [property: JsonPropertyName("emails")] string[]? Emails = null,
        [property: JsonPropertyName("phone")] string? Phone = null,
        [property: JsonPropertyName("location")] FacebookPageLocationResponse? Location = null,
        [property: JsonPropertyName("single_line_address")] string? SingleLineAddress = null,
        [property: JsonPropertyName("picture")] FacebookProfilePictureResponse? Picture = null);

    private sealed record FacebookPageLocationResponse(
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("zip")] string? Zip);

    private sealed record FacebookPagePostsResponse(
        [property: JsonPropertyName("summary")] FacebookPagePostsSummaryResponse? Summary);

    private sealed record FacebookPagePostsSummaryResponse(
        [property: JsonPropertyName("total_count")] int? TotalCount);
}
