using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Billing.Services;

public sealed class StripeCustomerResolver : IStripeCustomerResolver
{
    public const string CustomerMissingCode = "Stripe.CustomerMissing";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripePaymentService _stripePaymentService;

    public StripeCustomerResolver(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService)
    {
        _unitOfWork = unitOfWork;
        _userRepository = unitOfWork.Repository<User>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<StripeCustomerResolution>> ResolveAsync(
        Guid userId,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == userId && !item.IsDeleted, cancellationToken);

        if (user == null)
        {
            return Result.Failure<StripeCustomerResolution>(
                new Error("User.NotFound", "User not found."));
        }

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
        {
            return Result.Success(new StripeCustomerResolution(user, user.StripeCustomerId));
        }

        var stripeSubscriptionId = await ResolveExistingStripeSubscriptionIdAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            try
            {
                var snapshot = await _stripePaymentService.GetSubscriptionSnapshotAsync(
                    stripeSubscriptionId,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(snapshot.StripeCustomerId))
                {
                    await StoreStripeCustomerIdAsync(user, snapshot.StripeCustomerId, cancellationToken);
                    return Result.Success(new StripeCustomerResolution(user, snapshot.StripeCustomerId));
                }
            }
            catch (Exception ex)
            {
                return Result.Failure<StripeCustomerResolution>(
                    new Error("Stripe.CustomerResolveFailed", ex.Message));
            }
        }

        if (!createIfMissing)
        {
            return Result.Failure<StripeCustomerResolution>(
                new Error(CustomerMissingCode, "No Stripe customer is linked to this user."));
        }

        try
        {
            var customer = await _stripePaymentService.CreateCustomerAsync(
                user.Email,
                user.FullName ?? user.Username,
                new Dictionary<string, string>
                {
                    ["user_id"] = user.Id.ToString()
                },
                cancellationToken);

            await StoreStripeCustomerIdAsync(user, customer.StripeCustomerId, cancellationToken);
            return Result.Success(new StripeCustomerResolution(user, customer.StripeCustomerId));
        }
        catch (Exception ex)
        {
            return Result.Failure<StripeCustomerResolution>(
                new Error("Stripe.CustomerCreateFailed", ex.Message));
        }
    }

    private async Task<string?> ResolveExistingStripeSubscriptionIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _userSubscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                !item.IsDeleted &&
                item.StripeSubscriptionId != null &&
                item.StripeSubscriptionId != string.Empty)
            .OrderByDescending(item => item.ActiveDate ?? item.CreatedAt ?? item.UpdatedAt)
            .Select(item => item.StripeSubscriptionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task StoreStripeCustomerIdAsync(
        User user,
        string stripeCustomerId,
        CancellationToken cancellationToken)
    {
        user.StripeCustomerId = stripeCustomerId;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
