using Application.Abstractions.Data;
using Application.Abstractions.Instagram;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private readonly IUserSubscriptionEntitlementService _userSubscriptionEntitlementService;
    private readonly IRepository<User> _userRepository;

    public InitiateInstagramOAuthCommandHandler(
        IInstagramOAuthService instagramOAuthService,
        IUserSubscriptionEntitlementService userSubscriptionEntitlementService,
        IUnitOfWork unitOfWork)
    {
        _instagramOAuthService = instagramOAuthService;
        _userSubscriptionEntitlementService = userSubscriptionEntitlementService;
        _userRepository = unitOfWork.Repository<User>();
    }

    public async Task<Result<InstagramOAuthInitiationResponse>> Handle(
        InitiateInstagramOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var userExists = await _userRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(user => user.Id == request.UserId && !user.IsDeleted, cancellationToken);

        if (!userExists)
        {
            return Result.Failure<InstagramOAuthInitiationResponse>(
                new Error("User.NotFound", "User not found"));
        }

        var entitlementResult = await _userSubscriptionEntitlementService.EnsureSocialAccountLinkAllowedAsync(
            request.UserId,
            cancellationToken);

        if (entitlementResult.IsFailure)
        {
            return Result.Failure<InstagramOAuthInitiationResponse>(entitlementResult.Error);
        }

        var (authorizationUrl, state) =
            _instagramOAuthService.GenerateAuthorizationUrl(request.UserId, request.Scopes);

        var response = new InstagramOAuthInitiationResponse(authorizationUrl, state);
        return Result.Success(response);
    }
}
