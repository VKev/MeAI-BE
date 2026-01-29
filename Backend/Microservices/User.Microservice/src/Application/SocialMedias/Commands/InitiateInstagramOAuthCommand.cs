using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateInstagramOAuthCommand(Guid UserId, string? Scopes)
    : IRequest<Result<InstagramOAuthInitiationResponse>>;

public sealed record InstagramOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateInstagramOAuthCommandHandler
    : IRequestHandler<InitiateInstagramOAuthCommand, Result<InstagramOAuthInitiationResponse>>
{
    private const string AuthorizationBaseUrl = "https://www.facebook.com/v20.0/dialog/oauth";
    private const string DefaultScopes = "instagram_basic,pages_show_list";
    private readonly IConfiguration _configuration;

    public InitiateInstagramOAuthCommandHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<Result<InstagramOAuthInitiationResponse>> Handle(
        InitiateInstagramOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var appId = _configuration["Instagram:AppId"] ?? _configuration["Facebook:AppId"];
        var redirectUri = _configuration["Instagram:RedirectUri"] ?? _configuration["Facebook:RedirectUri"];
        var configId = _configuration["Instagram:ConfigId"];

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return Task.FromResult(Result.Failure<InstagramOAuthInitiationResponse>(new Error(
                "Instagram.NotConfigured",
                "Instagram AppId or RedirectUri is not configured.")));
        }

        var configuredScopes = _configuration["Instagram:Scopes"] ?? _configuration["Facebook:Scopes"];
        var scopes = string.IsNullOrWhiteSpace(request.Scopes)
            ? (string.IsNullOrWhiteSpace(configuredScopes) ? DefaultScopes : configuredScopes)
            : request.Scopes;

        var state = BuildState(request.UserId);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scopes,
            ["state"] = state
        };

        if (!string.IsNullOrWhiteSpace(configId))
        {
            queryParams["config_id"] = configId;
            queryParams["override_default_response_type"] = "true";
        }

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var url = $"{AuthorizationBaseUrl}?{queryString}";

        return Task.FromResult(Result.Success(new InstagramOAuthInitiationResponse(url, state)));
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
}
