using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Instagram;
using Application.Abstractions.Meta;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Instagram;

public sealed class InstagramOAuthService : IInstagramOAuthService
{
    private const string AuthorizationBaseUrl = "https://www.facebook.com/v24.0/dialog/oauth";
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v24.0";
    private const string OAuthTokenEndpoint = "https://graph.facebook.com/v24.0/oauth/access_token";
    private const string DefaultScopes = "instagram_basic,pages_show_list";

    private static readonly string[] RequiredScopes = { "pages_show_list", "instagram_basic" };

    private readonly string _appId;
    private readonly string _appSecret;
    private readonly string _redirectUri;
    private readonly string _scopes;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InstagramOAuthService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _appId = configuration["Instagram:AppId"] ?? configuration["Facebook:AppId"]
                 ?? throw new InvalidOperationException("Instagram:AppId is not configured");
        _appSecret = configuration["Instagram:AppSecret"] ?? configuration["Facebook:AppSecret"]
                     ?? throw new InvalidOperationException("Instagram:AppSecret is not configured");
        _redirectUri = configuration["Instagram:RedirectUri"] ?? configuration["Facebook:RedirectUri"]
                       ?? throw new InvalidOperationException("Instagram:RedirectUri is not configured");
        var configuredScopes = configuration["Instagram:Scopes"] ?? configuration["Facebook:Scopes"];
        _scopes = string.IsNullOrWhiteSpace(configuredScopes)
            ? DefaultScopes
            : configuredScopes;

        _httpClient = httpClientFactory.CreateClient("Instagram");
    }

    public (string AuthorizationUrl, string State) GenerateAuthorizationUrl(Guid userId, string? scopes)
    {
        var resolvedScopes = string.IsNullOrWhiteSpace(scopes)
            ? _scopes
            : scopes;

        var state = BuildState(userId);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _appId,
            ["redirect_uri"] = _redirectUri,
            ["response_type"] = "code",
            ["scope"] = resolvedScopes,
            ["state"] = state
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return ($"{AuthorizationBaseUrl}?{queryString}", state);
    }

    public async Task<Result<InstagramAccessTokenResponse>> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"{OAuthTokenEndpoint}?client_id={Uri.EscapeDataString(_appId)}&redirect_uri={Uri.EscapeDataString(_redirectUri)}&client_secret={Uri.EscapeDataString(_appSecret)}&code={Uri.EscapeDataString(code)}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ReadGraphApiError(payload) ?? "Failed to exchange Instagram code for access token.";
                return Result.Failure<InstagramAccessTokenResponse>(
                    new Error("Instagram.InvalidCode", errorMessage));
            }

            var token = JsonSerializer.Deserialize<InstagramAccessTokenResponse>(payload, JsonOptions);
            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return Result.Failure<InstagramAccessTokenResponse>(
                    new Error("Instagram.InvalidCode", "Failed to exchange Instagram code for access token."));
            }

            return Result.Success(token);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<InstagramAccessTokenResponse>(
                new Error("Instagram.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<InstagramAccessTokenResponse>(
                new Error("Instagram.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<MetaDebugToken>> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"{GraphApiBaseUrl}/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={_appId}|{_appSecret}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ReadGraphApiError(payload) ?? "Invalid Instagram access token";
                return Result.Failure<MetaDebugToken>(new Error("Instagram.InvalidToken", errorMessage));
            }

            var debugResponse = JsonSerializer.Deserialize<MetaDebugTokenResponse>(payload, JsonOptions);
            var debugToken = debugResponse?.Data;

            if (debugToken == null || !debugToken.IsValid)
            {
                return Result.Failure<MetaDebugToken>(
                    new Error("Instagram.InvalidToken", "Invalid Instagram access token."));
            }

            if (!string.IsNullOrWhiteSpace(debugToken.AppId) &&
                !string.Equals(debugToken.AppId, _appId, StringComparison.Ordinal))
            {
                return Result.Failure<MetaDebugToken>(
                    new Error("Instagram.AppMismatch", "Instagram token does not belong to this app."));
            }

            return Result.Success(debugToken);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<MetaDebugToken>(
                new Error("Instagram.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<MetaDebugToken>(
                new Error("Instagram.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<InstagramGraphProfileResult>> FetchBusinessProfileAsync(
        string userAccessToken,
        MetaDebugToken? debugToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var (pages, pagesError) = await FetchFacebookPagesAsync(userAccessToken, cancellationToken);
            if (!string.IsNullOrWhiteSpace(pagesError))
            {
                return Result.Failure<InstagramGraphProfileResult>(
                    new Error("Instagram.GraphApiError", pagesError));
            }

            if (pages.Count == 0)
            {
                var missingPermissions = debugToken == null
                    ? new List<string>()
                    : GetMissingPermissions(debugToken);

                if (missingPermissions.Count > 0)
                {
                    return Result.Failure<InstagramGraphProfileResult>(
                        new Error("Instagram.MissingPermissions",
                            $"Missing required permissions: {string.Join(", ", missingPermissions)}."));
                }

                return Result.Failure<InstagramGraphProfileResult>(
                    new Error("Instagram.NoPages", "No Facebook Pages were returned for this Facebook user."));
            }

            string? lastError = null;

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

                return Result.Success(new InstagramGraphProfileResult
                {
                    Profile = profile,
                    PageId = page.Id,
                    PageName = page.Name,
                    PageAccessToken = pageAccessToken,
                    InstagramAccountId = instagramAccountId!,
                    InstagramAccountType = instagramAccountType
                });
            }

            if (!string.IsNullOrWhiteSpace(lastError))
            {
                return Result.Failure<InstagramGraphProfileResult>(
                    new Error("Instagram.GraphApiError", lastError));
            }

            return Result.Failure<InstagramGraphProfileResult>(
                new Error("Instagram.NoBusinessAccount",
                    "No Instagram business or creator account found for this Facebook user."));
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<InstagramGraphProfileResult>(
                new Error("Instagram.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<InstagramGraphProfileResult>(
                new Error("Instagram.ParseError", $"JSON parse error: {ex.Message}"));
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

        var missing = new List<string>();

        foreach (var scope in RequiredScopes)
        {
            if (!grantedScopes.Contains(scope))
            {
                missing.Add(scope);
            }
        }

        return missing;
    }

    private async Task<(List<FacebookPage> Pages, string? ErrorMessage)> FetchFacebookPagesAsync(
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/me/accounts?fields=id,name,access_token,tasks,instagram_business_account,connected_instagram_account&access_token={Uri.EscapeDataString(userAccessToken)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ReadGraphApiError(payload);
            return (new List<FacebookPage>(), errorMessage);
        }

        var result = JsonSerializer.Deserialize<FacebookPagesResponse>(payload, JsonOptions);
        return (result?.Data ?? new List<FacebookPage>(), null);
    }

    private async Task<(string? AccessToken, string? ErrorMessage)> FetchPageAccessTokenAsync(
        string pageId,
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}?fields=access_token&access_token={Uri.EscapeDataString(userAccessToken)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ReadGraphApiError(payload);
            return (null, errorMessage);
        }

        var result = JsonSerializer.Deserialize<FacebookPageAccessTokenResponse>(payload, JsonOptions);
        return (result?.AccessToken, null);
    }

    private async Task<(InstagramAccountReference? Account, string? ErrorMessage)> FetchInstagramAccountAsync(
        string pageId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}?fields=instagram_business_account,connected_instagram_account&access_token={Uri.EscapeDataString(pageAccessToken)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ReadGraphApiError(payload);
            return (null, errorMessage);
        }

        var result = JsonSerializer.Deserialize<FacebookPageInstagramAccountResponse>(payload, JsonOptions);

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

    private async Task<(InstagramProfile? Profile, string? ErrorMessage)> FetchInstagramProfileAsync(
        string instagramAccountId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(instagramAccountId)}?fields=id,username&access_token={Uri.EscapeDataString(pageAccessToken)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ReadGraphApiError(payload);
            return (null, errorMessage);
        }

        var profile = JsonSerializer.Deserialize<InstagramProfile>(payload, JsonOptions);
        return (profile, null);
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

    private sealed record InstagramAccountReference(string Id, string Type);

    private sealed record GraphApiErrorResponse([property: JsonPropertyName("error")] GraphApiError? Error);

    private sealed record GraphApiError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
        [property: JsonPropertyName("error_user_title")] string? ErrorUserTitle,
        [property: JsonPropertyName("error_user_msg")] string? ErrorUserMessage,
        [property: JsonPropertyName("fbtrace_id")] string? FbTraceId);
}
