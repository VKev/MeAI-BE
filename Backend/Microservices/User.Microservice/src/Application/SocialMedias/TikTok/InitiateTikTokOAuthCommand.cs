using Application.Abstractions.TikTok;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.TikTok;

public sealed record InitiateTikTokOAuthCommand(
    Guid UserId,
    string Scopes) : IRequest<Result<TikTokOAuthInitiationResponse>>;

public sealed record TikTokOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateTikTokOAuthCommandHandler
    : IRequestHandler<InitiateTikTokOAuthCommand, Result<TikTokOAuthInitiationResponse>>
{
    private readonly ITikTokOAuthService _tikTokOAuthService;

    public InitiateTikTokOAuthCommandHandler(ITikTokOAuthService tikTokOAuthService)
    {
        _tikTokOAuthService = tikTokOAuthService;
    }

    public Task<Result<TikTokOAuthInitiationResponse>> Handle(
        InitiateTikTokOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var scopes = string.IsNullOrWhiteSpace(request.Scopes)
            ? "user.info.basic"
            : request.Scopes;

        var (authorizationUrl, state) = _tikTokOAuthService.GenerateAuthorizationUrl(request.UserId, scopes);

        var response = new TikTokOAuthInitiationResponse(authorizationUrl, state);
        return Task.FromResult(Result.Success(response));
    }
}
