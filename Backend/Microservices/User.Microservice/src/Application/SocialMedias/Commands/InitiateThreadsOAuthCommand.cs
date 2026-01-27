using Application.Abstractions.Threads;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateThreadsOAuthCommand(
    Guid UserId,
    string Scopes) : IRequest<Result<ThreadsOAuthInitiationResponse>>;

public sealed record ThreadsOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateThreadsOAuthCommandHandler
    : IRequestHandler<InitiateThreadsOAuthCommand, Result<ThreadsOAuthInitiationResponse>>
{
    private readonly IThreadsOAuthService _threadsOAuthService;

    public InitiateThreadsOAuthCommandHandler(IThreadsOAuthService threadsOAuthService)
    {
        _threadsOAuthService = threadsOAuthService;
    }

    public Task<Result<ThreadsOAuthInitiationResponse>> Handle(
        InitiateThreadsOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var scopes = string.IsNullOrWhiteSpace(request.Scopes)
            ? "threads_basic"
            : request.Scopes;

        var (authorizationUrl, state) = _threadsOAuthService.GenerateAuthorizationUrl(request.UserId, scopes);

        var response = new ThreadsOAuthInitiationResponse(authorizationUrl, state);
        return Task.FromResult(Result.Success(response));
    }
}
