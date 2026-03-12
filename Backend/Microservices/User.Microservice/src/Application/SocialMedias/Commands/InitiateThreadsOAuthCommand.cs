using Application.Abstractions.Data;
using Application.Abstractions.Threads;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateThreadsOAuthCommand(
    Guid UserId,
    string? Scopes) : IRequest<Result<ThreadsOAuthInitiationResponse>>;

public sealed record ThreadsOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateThreadsOAuthCommandHandler
    : IRequestHandler<InitiateThreadsOAuthCommand, Result<ThreadsOAuthInitiationResponse>>
{
    private readonly IThreadsOAuthService _threadsOAuthService;
    private readonly IRepository<User> _userRepository;

    public InitiateThreadsOAuthCommandHandler(
        IThreadsOAuthService threadsOAuthService,
        IUnitOfWork unitOfWork)
    {
        _threadsOAuthService = threadsOAuthService;
        _userRepository = unitOfWork.Repository<User>();
    }

    public async Task<Result<ThreadsOAuthInitiationResponse>> Handle(
        InitiateThreadsOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var userExists = await _userRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(user => user.Id == request.UserId && !user.IsDeleted, cancellationToken);

        if (!userExists)
        {
            return Result.Failure<ThreadsOAuthInitiationResponse>(
                new Error("User.NotFound", "User not found"));
        }

        var (authorizationUrl, state) = _threadsOAuthService.GenerateAuthorizationUrl(request.UserId, request.Scopes);

        var response = new ThreadsOAuthInitiationResponse(authorizationUrl, state);
        return Result.Success(response);
    }
}
