using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Threads;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Threads;

public sealed class ThreadsOAuthService : IThreadsOAuthService
{
    private const string AuthorizationBaseUrl = "https://threads.net/oauth/authorize";
    private const string TokenEndpoint = "https://graph.threads.net/oauth/access_token";
    private const string LongLivedTokenEndpoint = "https://graph.threads.net/access_token";
    private const string RefreshTokenEndpoint = "https://graph.threads.net/refresh_access_token";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ThreadsOAuthService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _clientId = configuration["Threads:AppId"]
                     ?? throw new InvalidOperationException("Threads:AppId is not configured");
        _clientSecret = configuration["Threads:AppSecret"]
                        ?? throw new InvalidOperationException("Threads:AppSecret is not configured");
        _redirectUri = configuration["Threads:RedirectUri"]
                       ?? throw new InvalidOperationException("Threads:RedirectUri is not configured");
        _httpClient = httpClientFactory.CreateClient("Threads");
    }

    public (string AuthorizationUrl, string State) GenerateAuthorizationUrl(Guid userId, string scopes)
    {
        var stateData = $"{userId}|{GenerateRandomString(16)}";
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(stateData));

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = scopes,
            ["response_type"] = "code",
            ["redirect_uri"] = _redirectUri,
            ["state"] = state
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return ($"{AuthorizationBaseUrl}?{queryString}", state);
    }

    public async Task<Result<ThreadsTokenResponse>> ExchangeCodeForTokenAsync(string code,
        CancellationToken cancellationToken)
    {
        // Step 1: Exchange authorization code for short-lived access token
        var formData = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _redirectUri
        };

        var shortLivedTokenResult = await SendTokenRequestAsync(TokenEndpoint, formData, cancellationToken);
        if (shortLivedTokenResult.IsFailure)
        {
            return shortLivedTokenResult;
        }

        // Step 2: Exchange short-lived token for long-lived token
        var shortLivedToken = shortLivedTokenResult.Value.AccessToken;
        var longLivedTokenUrl =
            $"{LongLivedTokenEndpoint}?grant_type=th_exchange_token&client_secret={Uri.EscapeDataString(_clientSecret)}&access_token={Uri.EscapeDataString(shortLivedToken)}";

        return await SendGetTokenRequestAsync(longLivedTokenUrl, shortLivedTokenResult.Value.UserId,
            cancellationToken);
    }

    public async Task<Result<ThreadsTokenResponse>> RefreshTokenAsync(string accessToken,
        CancellationToken cancellationToken)
    {
        var refreshUrl =
            $"{RefreshTokenEndpoint}?grant_type=th_refresh_token&access_token={Uri.EscapeDataString(accessToken)}";

        return await SendGetTokenRequestAsync(refreshUrl, null, cancellationToken);
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

    public async Task<Result<ThreadsUserProfile>> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        const string baseUrl = "https://graph.threads.net/me";
        var url = $"{baseUrl}?fields=id,username,name,threads_profile_picture_url,threads_biography&access_token={Uri.EscapeDataString(accessToken)}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<ThreadsErrorResponse>(responseBody, JsonOptions);
                return Result.Failure<ThreadsUserProfile>(
                    new Error("Threads.ProfileError", error?.ErrorMessage ?? $"Failed to fetch profile: {response.StatusCode}"));
            }

            var profile = JsonSerializer.Deserialize<ThreadsApiProfileResponse>(responseBody, JsonOptions);

            return Result.Success(new ThreadsUserProfile
            {
                Id = profile?.Id,
                Username = profile?.Username,
                Name = profile?.Name,
                ThreadsProfilePictureUrl = profile?.ThreadsProfilePictureUrl,
                ThreadsBiography = profile?.ThreadsBiography
            });
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<ThreadsUserProfile>(
                new Error("Threads.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<ThreadsUserProfile>(
                new Error("Threads.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private async Task<Result<ThreadsTokenResponse>> SendTokenRequestAsync(
        string endpoint,
        Dictionary<string, string> formData,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<ThreadsErrorResponse>(responseBody, JsonOptions);
                return Result.Failure<ThreadsTokenResponse>(
                    new Error("Threads.TokenError", error?.ErrorMessage ?? "Unknown Threads error"));
            }

            var tokenResponse = JsonSerializer.Deserialize<ThreadsApiTokenResponse>(responseBody, JsonOptions);

            if (tokenResponse == null)
            {
                return Result.Failure<ThreadsTokenResponse>(
                    new Error("Threads.ParseError", "Failed to parse Threads token response"));
            }

            return Result.Success(new ThreadsTokenResponse
            {
                UserId = tokenResponse.UserId ?? string.Empty,
                AccessToken = tokenResponse.AccessToken ?? string.Empty,
                ExpiresIn = tokenResponse.ExpiresIn,
                TokenType = tokenResponse.TokenType ?? "bearer"
            });
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<ThreadsTokenResponse>(
                new Error("Threads.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<ThreadsTokenResponse>(
                new Error("Threads.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private async Task<Result<ThreadsTokenResponse>> SendGetTokenRequestAsync(
        string url,
        string? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<ThreadsErrorResponse>(responseBody, JsonOptions);
                return Result.Failure<ThreadsTokenResponse>(
                    new Error("Threads.TokenError", error?.ErrorMessage ?? "Unknown Threads error"));
            }

            var tokenResponse = JsonSerializer.Deserialize<ThreadsApiTokenResponse>(responseBody, JsonOptions);

            if (tokenResponse == null)
            {
                return Result.Failure<ThreadsTokenResponse>(
                    new Error("Threads.ParseError", "Failed to parse Threads token response"));
            }

            return Result.Success(new ThreadsTokenResponse
            {
                UserId = userId ?? tokenResponse.UserId ?? string.Empty,
                AccessToken = tokenResponse.AccessToken ?? string.Empty,
                ExpiresIn = tokenResponse.ExpiresIn,
                TokenType = tokenResponse.TokenType ?? "bearer"
            });
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<ThreadsTokenResponse>(
                new Error("Threads.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<ThreadsTokenResponse>(
                new Error("Threads.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)[..length].Replace("+", "").Replace("/", "").Replace("=", "");
    }

    private sealed class ThreadsApiTokenResponse
    {
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private sealed class ThreadsApiProfileResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("threads_profile_picture_url")]
        public string? ThreadsProfilePictureUrl { get; set; }

        [JsonPropertyName("threads_biography")]
        public string? ThreadsBiography { get; set; }
    }
}

