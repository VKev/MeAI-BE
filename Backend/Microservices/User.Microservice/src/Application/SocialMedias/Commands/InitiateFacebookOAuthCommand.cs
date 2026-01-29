using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateFacebookOAuthCommand(Guid UserId, string? Scopes)
    : IRequest<Result<FacebookOAuthInitiationResponse>>;

public sealed record FacebookOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateFacebookOAuthCommandHandler
    : IRequestHandler<InitiateFacebookOAuthCommand, Result<FacebookOAuthInitiationResponse>>
{
    private const string DefaultScopes = "email,public_profile";
    private readonly IConfiguration _configuration;

    public InitiateFacebookOAuthCommandHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<Result<FacebookOAuthInitiationResponse>> Handle(
        InitiateFacebookOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var appId = _configuration["Facebook:AppId"];
        var redirectUri = _configuration["Facebook:RedirectUri"];
        var configId = _configuration["Facebook:ConfigId"];

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return Task.FromResult(Result.Failure<FacebookOAuthInitiationResponse>(new Error(
                "Facebook.NotConfigured",
                "Facebook AppId or RedirectUri is not configured.")));
        }

        var configuredScopes = _configuration["Facebook:Scopes"];
        var scopes = string.IsNullOrWhiteSpace(request.Scopes)
            ? (string.IsNullOrWhiteSpace(configuredScopes) ? DefaultScopes : configuredScopes)
            : request.Scopes;

        var state = BuildState(request.UserId);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["scope"] = scopes
        };

        if (!string.IsNullOrWhiteSpace(configId))
        {
            queryParams["config_id"] = configId;
            queryParams["override_default_response_type"] = "true";
        }

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var url = $"https://www.facebook.com/v20.0/dialog/oauth?{queryString}";

        return Task.FromResult(Result.Success(new FacebookOAuthInitiationResponse(url, state)));
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
