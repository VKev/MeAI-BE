using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private readonly string _appId;
    private readonly string _appSecret;
    private readonly string _redirectUri;
    private readonly string _scopes;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FacebookOAuthService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _appId = configuration["Facebook:AppId"]
                 ?? throw new InvalidOperationException("Facebook:AppId is not configured");
        _appSecret = configuration["Facebook:AppSecret"]
                     ?? throw new InvalidOperationException("Facebook:AppSecret is not configured");
        _redirectUri = configuration["Facebook:RedirectUri"]
                       ?? throw new InvalidOperationException("Facebook:RedirectUri is not configured");
        var configuredScopes = configuration["Facebook:Scopes"];
        _scopes = string.IsNullOrWhiteSpace(configuredScopes)
            ? DefaultScopes
            : configuredScopes;

        _httpClient = httpClientFactory.CreateClient("Facebook");
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
            var url =
                $"{OAuthTokenEndpoint}?client_id={Uri.EscapeDataString(_appId)}&redirect_uri={Uri.EscapeDataString(_redirectUri)}&client_secret={Uri.EscapeDataString(_appSecret)}&code={Uri.EscapeDataString(code)}";

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
            var url =
                $"{GraphApiBaseUrl}/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={_appId}|{_appSecret}";

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
                !string.Equals(debugToken.AppId, _appId, StringComparison.Ordinal))
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
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"{GraphApiBaseUrl}/me?fields=id,name,email&access_token={Uri.EscapeDataString(accessToken)}";

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
}
