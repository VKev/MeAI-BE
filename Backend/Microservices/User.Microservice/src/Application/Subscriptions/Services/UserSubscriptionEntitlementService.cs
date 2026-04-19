using Application.Abstractions.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Services;

public sealed class UserSubscriptionEntitlementService : IUserSubscriptionEntitlementService
{
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IUserSubscriptionStateService _userSubscriptionStateService;
    private readonly IRepository<Workspace> _workspaceRepository;

    public UserSubscriptionEntitlementService(
        IUnitOfWork unitOfWork,
        IUserSubscriptionStateService userSubscriptionStateService)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionStateService = userSubscriptionStateService;
        _workspaceRepository = unitOfWork.Repository<Workspace>();
    }

    public async Task<UserSubscriptionEntitlement> GetCurrentEntitlementAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var state = await _userSubscriptionStateService.GetStateAsync(userId, cancellationToken);

        if (state.Current == null)
        {
            return new UserSubscriptionEntitlement(null, null);
        }

        var currentPlan = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == state.Current.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        return new UserSubscriptionEntitlement(state.Current, currentPlan);
    }

    public async Task<Result<UserSubscriptionEntitlement>> EnsureWorkspaceCreationAllowedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entitlement = await GetCurrentEntitlementAsync(userId, cancellationToken);

        if (entitlement.MaxWorkspaces <= 0)
        {
            return Result.Failure<UserSubscriptionEntitlement>(
                new Error("Workspace.LimitUnavailable", "Your current plan does not include workspace access."));
        }

        var currentWorkspaceCount = await _workspaceRepository.GetAll()
            .AsNoTracking()
            .CountAsync(
                item => item.UserId == userId && !item.IsDeleted,
                cancellationToken);

        if (currentWorkspaceCount >= entitlement.MaxWorkspaces)
        {
            return Result.Failure<UserSubscriptionEntitlement>(
                new Error(
                    "Workspace.LimitExceeded",
                    $"Your current plan allows up to {entitlement.MaxWorkspaces} workspace(s). Upgrade to create more."));
        }

        return Result.Success(entitlement);
    }

    public async Task<Result<UserSubscriptionEntitlement>> EnsureSocialAccountLinkAllowedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entitlement = await GetCurrentEntitlementAsync(userId, cancellationToken);

        if (entitlement.MaxSocialAccounts <= 0)
        {
            return Result.Failure<UserSubscriptionEntitlement>(
                new Error("SocialMedia.LimitUnavailable", "Your current plan does not include social account linking."));
        }

        var currentSocialAccountCount = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == userId && !item.IsDeleted)
            .Select(item => item.Type)
            .Distinct()
            .CountAsync(cancellationToken);

        if (currentSocialAccountCount >= entitlement.MaxSocialAccounts)
        {
            return Result.Failure<UserSubscriptionEntitlement>(
                new Error(
                    "SocialMedia.LimitExceeded",
                    $"Your current plan allows up to {entitlement.MaxSocialAccounts} linked social account(s). Upgrade to add more."));
        }

        return Result.Success(entitlement);
    }
}
