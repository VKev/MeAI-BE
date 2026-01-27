using Application.Abstractions.TikTok;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateTikTokOAuthCommand(
    Guid UserId,
    string Scopes) : IRequest<Result<TikTokOAuthInitiationResponse>>;

public sealed record TikTokOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateTikTokOAuthCommandHandler
    : IRequestHandler<InitiateTikTokOAuthCommand, Result<TikTokOAuthInitiationResponse>>
{
    private readonly ITikTokOAuthService _tikTokOAuthService;
    private readonly IMemoryCache _memoryCache;

    public InitiateTikTokOAuthCommandHandler(
        ITikTokOAuthService tikTokOAuthService,
        IMemoryCache memoryCache)
    {
        _tikTokOAuthService = tikTokOAuthService;
        _memoryCache = memoryCache;
    }

    public Task<Result<TikTokOAuthInitiationResponse>> Handle(
        InitiateTikTokOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var scopes = string.IsNullOrWhiteSpace(request.Scopes)
            ? "user.info.basic"
            : request.Scopes;

        var (authorizationUrl, state, codeVerifier) = _tikTokOAuthService.GenerateAuthorizationUrl(request.UserId, scopes);

        // Store code_verifier in cache with state as key for 10 minutes
        _memoryCache.Set($"tiktok_code_verifier_{state}", codeVerifier, TimeSpan.FromMinutes(10));

        var response = new TikTokOAuthInitiationResponse(authorizationUrl, state);
        return Task.FromResult(Result.Success(response));
    }
}
