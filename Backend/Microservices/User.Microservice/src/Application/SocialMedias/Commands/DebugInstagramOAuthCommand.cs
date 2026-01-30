using System.Text.Json;
using System.Text.Json.Serialization;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record DebugInstagramOAuthCommand(Guid UserId, string Code)
    : IRequest<Result<InstagramOAuthDebugResponse>>;

public sealed class DebugInstagramOAuthCommandHandler
    : IRequestHandler<DebugInstagramOAuthCommand, Result<InstagramOAuthDebugResponse>>
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";
    private const string OAuthTokenEndpoint = "https://graph.facebook.com/v21.0/oauth/access_token";
    private static readonly Type DomainDependency = typeof(User);

    private readonly IConfiguration _configuration;

    public DebugInstagramOAuthCommandHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<Result<InstagramOAuthDebugResponse>> Handle(
        DebugInstagramOAuthCommand request,
        CancellationToken cancellationToken)
    {
        _ = DomainDependency;

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<InstagramOAuthDebugResponse>(
                new Error("Instagram.MissingCode", "Authorization code is missing"));
        }

        var appId = _configuration["Instagram:AppId"] ?? _configuration["Facebook:AppId"];
        var appSecret = _configuration["Instagram:AppSecret"] ?? _configuration["Facebook:AppSecret"];
        var redirectUri = _configuration["Instagram:RedirectUri"] ?? _configuration["Facebook:RedirectUri"];

        if (string.IsNullOrWhiteSpace(appId) ||
            string.IsNullOrWhiteSpace(appSecret) ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            return Result.Failure<InstagramOAuthDebugResponse>(
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
            return Result.Failure<InstagramOAuthDebugResponse>(
                new Error("Instagram.InvalidCode", "Failed to exchange Instagram code for access token."));
        }

        var accessToken = tokenResponse.AccessToken!;

        var (debugToken, tokenError) = await ValidateTokenAsync(
            accessToken,
            appId,
            appSecret,
            cancellationToken);

        var tokenInfo = debugToken == null
            ? null
            : new InstagramDebugTokenInfo(
                debugToken.IsValid,
                debugToken.AppId,
                debugToken.Scopes ?? new List<string>(),
                debugToken.GranularScopes?.Select(scope =>
                        new InstagramDebugGranularScope(
                            scope.Scope ?? string.Empty,
                            scope.TargetIds ?? new List<string>()))
                    .ToList() ?? new List<InstagramDebugGranularScope>());

        var missingPermissions = debugToken == null
            ? new List<string>()
            : GetMissingPermissions(debugToken);

        var (pages, pagesError) = await FetchFacebookPagesAsync(accessToken, cancellationToken);
        var debugPages = pages.Select(page =>
                new InstagramDebugPage(
                    page.Id,
                    page.Name,
                    page.Tasks ?? new List<string>(),
                    !string.IsNullOrWhiteSpace(page.AccessToken),
                    page.InstagramBusinessAccount?.Id,
                    page.ConnectedInstagramAccount?.Id))
            .ToList();

        var response = new InstagramOAuthDebugResponse(
            tokenInfo,
            missingPermissions,
            new InstagramDebugPagesResponse(debugPages.Count, debugPages, pagesError),
            tokenError);

        return Result.Success(response);
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

    private static async Task<(MetaDebugToken? Token, string? ErrorMessage)> ValidateTokenAsync(
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
            var errorMessage = await ReadGraphApiErrorAsync(response, cancellationToken);
            return (null, errorMessage);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var debug = JsonSerializer.Deserialize<MetaDebugTokenResponse>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return (debug?.Data, null);
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

    private sealed record InstagramBusinessAccount([property: JsonPropertyName("id")] string? Id);

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
}
