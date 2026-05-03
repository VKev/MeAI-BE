using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetCurrentSubscriptionEntitlementsQuery(Guid UserId)
    : IRequest<Result<CurrentSubscriptionEntitlementsResponse>>;

public sealed class GetCurrentSubscriptionEntitlementsQueryHandler
    : IRequestHandler<GetCurrentSubscriptionEntitlementsQuery, Result<CurrentSubscriptionEntitlementsResponse>>
{
    private readonly IUserSubscriptionEntitlementService _userSubscriptionEntitlementService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Workspace> _workspaceRepository;

    public GetCurrentSubscriptionEntitlementsQueryHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService userSubscriptionEntitlementService)
    {
        _userSubscriptionEntitlementService = userSubscriptionEntitlementService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _workspaceRepository = unitOfWork.Repository<Workspace>();
    }

    public async Task<Result<CurrentSubscriptionEntitlementsResponse>> Handle(
        GetCurrentSubscriptionEntitlementsQuery request,
        CancellationToken cancellationToken)
    {
        var userExists = await _userRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(item => item.Id == request.UserId && !item.IsDeleted, cancellationToken);

        if (!userExists)
        {
            return Result.Failure<CurrentSubscriptionEntitlementsResponse>(
                new Error("User.NotFound", "User not found"));
        }

        var entitlement = await _userSubscriptionEntitlementService.GetCurrentEntitlementAsync(
            request.UserId,
            cancellationToken);

        var currentSocialAccounts = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == request.UserId && !item.IsDeleted)
            .Select(item => item.Type)
            .Distinct()
            .CountAsync(cancellationToken);

        var currentWorkspaceCount = await _workspaceRepository.GetAll()
            .AsNoTracking()
            .CountAsync(item => item.UserId == request.UserId && !item.IsDeleted, cancellationToken);

        var maxSocialAccounts = entitlement.MaxSocialAccounts;
        var response = new CurrentSubscriptionEntitlementsResponse(
            entitlement.HasActivePlan,
            entitlement.CurrentSubscription?.Id,
            entitlement.CurrentPlan?.Id,
            entitlement.CurrentPlan?.Name,
            maxSocialAccounts,
            currentSocialAccounts,
            Math.Max(0, maxSocialAccounts - currentSocialAccounts),
            entitlement.MaxPagesPerSocialAccount,
            currentWorkspaceCount,
            entitlement.MaxWorkspaces);

        return Result.Success(response);
    }
}
