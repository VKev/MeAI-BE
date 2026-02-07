using Application.Abstractions.Data;
using Application.Abstractions.Facebook;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private readonly IRepository<User> _userRepository;

    public InitiateFacebookOAuthCommandHandler(
        IFacebookOAuthService facebookOAuthService,
        IUnitOfWork unitOfWork)
    {
        _facebookOAuthService = facebookOAuthService;
        _userRepository = unitOfWork.Repository<User>();
    }

    public async Task<Result<FacebookOAuthInitiationResponse>> Handle(
        InitiateFacebookOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var userExists = await _userRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(user => user.Id == request.UserId && !user.IsDeleted, cancellationToken);

        if (!userExists)
        {
            return Result.Failure<FacebookOAuthInitiationResponse>(
                new Error("User.NotFound", "User not found"));
        }

        var (authorizationUrl, state) =
            _facebookOAuthService.GenerateAuthorizationUrl(request.UserId, request.Scopes);

        var response = new FacebookOAuthInitiationResponse(authorizationUrl, state);
        return Result.Success(response);
    }
}
