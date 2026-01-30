using Application.Abstractions.Facebook;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateFacebookOAuthCommand(Guid UserId, string? Scopes)
    : IRequest<Result<FacebookOAuthInitiationResponse>>;

public sealed record FacebookOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateFacebookOAuthCommandHandler
    : IRequestHandler<InitiateFacebookOAuthCommand, Result<FacebookOAuthInitiationResponse>>
{
    private readonly IFacebookOAuthService _facebookOAuthService;

    public InitiateFacebookOAuthCommandHandler(IFacebookOAuthService facebookOAuthService)
    {
        _facebookOAuthService = facebookOAuthService;
    }

    public Task<Result<FacebookOAuthInitiationResponse>> Handle(
        InitiateFacebookOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var (authorizationUrl, state) =
            _facebookOAuthService.GenerateAuthorizationUrl(request.UserId, request.Scopes);

        var response = new FacebookOAuthInitiationResponse(authorizationUrl, state);
        return Task.FromResult(Result.Success(response));
    }
}
