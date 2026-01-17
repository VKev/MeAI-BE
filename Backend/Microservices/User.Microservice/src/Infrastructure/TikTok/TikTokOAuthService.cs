using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.TikTok;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.TikTok;

public sealed class TikTokOAuthService : ITikTokOAuthService
{
    private const string AuthorizationBaseUrl = "https://www.tiktok.com/v2/auth/authorize/";
    private const string TokenEndpoint = "https://open.tiktokapis.com/v2/oauth/token/";

    private readonly string _clientKey;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TikTokOAuthService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _clientKey = configuration["TikTok:ClientKey"]
                     ?? throw new InvalidOperationException("TikTok:ClientKey is not configured");
        _clientSecret = configuration["TikTok:ClientSecret"]
                        ?? throw new InvalidOperationException("TikTok:ClientSecret is not configured");
        _redirectUri = configuration["TikTok:RedirectUri"]
                       ?? throw new InvalidOperationException("TikTok:RedirectUri is not configured");
        _httpClient = httpClientFactory.CreateClient("TikTok");
    }

    public (string AuthorizationUrl, string State, string CodeVerifier) GenerateAuthorizationUrl(Guid userId, string scopes)
    {
        var stateData = $"{userId}|{GenerateRandomString(16)}";
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(stateData));

        // Generate PKCE parameters
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var queryParams = new Dictionary<string, string>
        {
            ["client_key"] = _clientKey,
            ["scope"] = scopes,
            ["response_type"] = "code",
            ["redirect_uri"] = _redirectUri,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return ($"{AuthorizationBaseUrl}?{queryString}", state, codeVerifier);
    }

    public async Task<Result<TikTokTokenResponse>> ExchangeCodeForTokenAsync(string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var formData = new Dictionary<string, string>
        {
            ["client_key"] = _clientKey,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _redirectUri,
            ["code_verifier"] = codeVerifier
        };

        return await SendTokenRequestAsync(formData, cancellationToken);
    }

    public async Task<Result<TikTokTokenResponse>> RefreshTokenAsync(string refreshToken,
        CancellationToken cancellationToken)
    {
        var formData = new Dictionary<string, string>
        {
            ["client_key"] = _clientKey,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };

        return await SendTokenRequestAsync(formData, cancellationToken);
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

    private async Task<Result<TikTokTokenResponse>> SendTokenRequestAsync(
        Dictionary<string, string> formData,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<TikTokErrorResponse>(responseBody, JsonOptions);
                return Result.Failure<TikTokTokenResponse>(
                    new Error("TikTok.TokenError", error?.ErrorDescription ?? "Unknown TikTok error"));
            }

            var tokenResponse = JsonSerializer.Deserialize<TikTokApiTokenResponse>(responseBody, JsonOptions);

            if (tokenResponse == null)
            {
                return Result.Failure<TikTokTokenResponse>(
                    new Error("TikTok.ParseError", "Failed to parse TikTok token response"));
            }

            return Result.Success(new TikTokTokenResponse
            {
                OpenId = tokenResponse.OpenId ?? string.Empty,
                AccessToken = tokenResponse.AccessToken ?? string.Empty,
                RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
                ExpiresIn = tokenResponse.ExpiresIn,
                RefreshExpiresIn = tokenResponse.RefreshExpiresIn,
                Scope = tokenResponse.Scope ?? string.Empty,
                TokenType = tokenResponse.TokenType ?? "Bearer"
            });
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<TikTokTokenResponse>(
                new Error("TikTok.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<TikTokTokenResponse>(
                new Error("TikTok.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)[..length].Replace("+", "").Replace("/", "").Replace("=", "");
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32]; // 32 bytes = 43 characters in base64url
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private sealed class TikTokApiTokenResponse
    {
        [JsonPropertyName("open_id")]
        public string? OpenId { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_expires_in")]
        public int RefreshExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
