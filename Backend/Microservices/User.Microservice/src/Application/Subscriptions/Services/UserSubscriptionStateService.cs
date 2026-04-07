using Application.Abstractions.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Services;

public sealed class UserSubscriptionStateService : IUserSubscriptionStateService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;

    public UserSubscriptionStateService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
    }

    public async Task<UserSubscriptionState> GetStateAsync(
        Guid userId,
        CancellationToken cancellationToken,
        bool persistChanges = false)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var subscriptions = await _userSubscriptionRepository.GetAll()
            .Where(item => item.UserId == userId && !item.IsDeleted)
            .OrderByDescending(item => item.ActiveDate ?? item.CreatedAt ?? item.UpdatedAt)
            .ThenByDescending(item => item.EndDate)
            .ToListAsync(cancellationToken);

        var changed = false;
        var activeSubscriptions = subscriptions
            .Where(item => IsCurrentlyActive(item, now))
            .OrderByDescending(item => item.ActiveDate ?? item.CreatedAt ?? item.UpdatedAt)
            .ThenByDescending(item => item.EndDate)
            .ToList();

        var current = activeSubscriptions.FirstOrDefault();

        foreach (var overlappingActive in activeSubscriptions.Skip(1))
        {
            overlappingActive.Status = "Superseded";
            if (!overlappingActive.EndDate.HasValue || overlappingActive.EndDate.Value > now)
            {
                overlappingActive.EndDate = now;
            }

            overlappingActive.UpdatedAt = now;
            changed = true;
        }

        foreach (var expiredSubscription in subscriptions.Where(item =>
                     IsActiveStatus(item.Status) &&
                     item.EndDate.HasValue &&
                     item.EndDate.Value <= now &&
                     (current == null || item.Id != current.Id)))
        {
            if (!string.Equals(expiredSubscription.Status, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                expiredSubscription.Status = "Expired";
                expiredSubscription.UpdatedAt = now;
                changed = true;
            }
        }

        current = subscriptions
            .Where(item => IsCurrentlyActive(item, now))
            .OrderByDescending(item => item.ActiveDate ?? item.CreatedAt ?? item.UpdatedAt)
            .ThenByDescending(item => item.EndDate)
            .FirstOrDefault();

        var scheduled = subscriptions
            .Where(item =>
                IsScheduledStatus(item.Status) &&
                (!current?.Id.Equals(item.Id) ?? true))
            .OrderBy(item => item.ActiveDate ?? item.CreatedAt ?? item.UpdatedAt)
            .ThenBy(item => item.CreatedAt)
            .FirstOrDefault();

        if (persistChanges && changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new UserSubscriptionState(current, scheduled);
    }

    private static bool IsCurrentlyActive(UserSubscription subscription, DateTime now)
    {
        if (!IsActiveStatus(subscription.Status))
        {
            return false;
        }

        if (subscription.ActiveDate.HasValue && subscription.ActiveDate.Value > now)
        {
            return false;
        }

        return !subscription.EndDate.HasValue || subscription.EndDate.Value > now;
    }

    private static bool IsActiveStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static bool IsScheduledStatus(string? status) =>
        string.Equals(status, "Scheduled", StringComparison.OrdinalIgnoreCase);
}
