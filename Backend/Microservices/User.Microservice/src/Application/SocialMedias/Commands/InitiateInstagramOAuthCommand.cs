using Application.Abstractions.Instagram;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateInstagramOAuthCommand(Guid UserId, string? Scopes)
    : IRequest<Result<InstagramOAuthInitiationResponse>>;

public sealed record InstagramOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateInstagramOAuthCommandHandler
    : IRequestHandler<InitiateInstagramOAuthCommand, Result<InstagramOAuthInitiationResponse>>
{
    private readonly IInstagramOAuthService _instagramOAuthService;

    public InitiateInstagramOAuthCommandHandler(IInstagramOAuthService instagramOAuthService)
    {
        _instagramOAuthService = instagramOAuthService;
    }

    public Task<Result<InstagramOAuthInitiationResponse>> Handle(
        InitiateInstagramOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var (authorizationUrl, state) =
            _instagramOAuthService.GenerateAuthorizationUrl(request.UserId, request.Scopes);

        var response = new InstagramOAuthInitiationResponse(authorizationUrl, state);
        return Task.FromResult(Result.Success(response));
    }
}
