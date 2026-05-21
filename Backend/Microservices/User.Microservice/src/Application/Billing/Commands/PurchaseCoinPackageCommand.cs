using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Billing.Models;
using Application.Billing.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLibrary.Configs;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;
using Application.Billing.Commands;

namespace Application.Billing.Commands;

public sealed record PurchaseCoinPackageCommand(
    Guid PackageId,
    Guid UserId,
    bool UseDefaultCard = false) : IRequest<Result<CoinPackageCheckoutResponse>>;

public sealed class PurchaseCoinPackageCommandHandler
    : IRequestHandler<PurchaseCoinPackageCommand, Result<CoinPackageCheckoutResponse>>
{
    private readonly IRepository<CoinPackage> _coinPackageRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IStripeCustomerResolver _stripeCustomerResolver;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ISender _sender;
    private readonly string _configuredCurrency;

    // Domain dependency marker for architecture tests
    private static readonly Type CoinPackageEntityType = typeof(CoinPackage);

    public PurchaseCoinPackageCommandHandler(
        IUnitOfWork unitOfWork,
        IStripeCustomerResolver stripeCustomerResolver,
        IStripePaymentService stripePaymentService,
        IOptions<BillingCurrencyOptions> billingCurrencyOptions,
        ISender sender)
    {
        _coinPackageRepository = unitOfWork.Repository<CoinPackage>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _stripeCustomerResolver = stripeCustomerResolver;
        _stripePaymentService = stripePaymentService;
        _sender = sender;
        _configuredCurrency = ResolveCurrency(billingCurrencyOptions.Value);
    }

    public async Task<Result<CoinPackageCheckoutResponse>> Handle(
        PurchaseCoinPackageCommand request,
        CancellationToken cancellationToken)
    {
        var package = await _coinPackageRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.PackageId, cancellationToken);

        if (package == null || !package.IsActive)
        {
            return Result.Failure<CoinPackageCheckoutResponse>(
                new Error("CoinPackage.NotFound", "Coin package not found or inactive."));
        }

        if (package.Price <= 0m)
        {
            return Result.Failure<CoinPackageCheckoutResponse>(
                new Error("CoinPackage.InvalidPrice", "Coin package price is not valid."));
        }

        if (!string.Equals(package.Currency, _configuredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<CoinPackageCheckoutResponse>(
                new Error("CoinPackage.InvalidCurrency", "Coin package currency is not supported."));
        }

        var customerResult = await _stripeCustomerResolver.ResolveAsync(
            request.UserId,
            createIfMissing: true,
            cancellationToken);

        if (customerResult.IsFailure)
        {
            return Result.Failure<CoinPackageCheckoutResponse>(customerResult.Error);
        }

        string? defaultPaymentMethodId = null;
        if (request.UseDefaultCard)
        {
            var pmId = await _stripePaymentService.GetCustomerDefaultPaymentMethodIdAsync(
                customerResult.Value.StripeCustomerId,
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(pmId))
            {
                defaultPaymentMethodId = pmId;
            }
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            RelationId = package.Id,
            RelationType = "CoinPackage",
            Cost = package.Price,
            TransactionType = "coin_package_purchase",
            PaymentMethod = "Stripe",
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        try
        {
            var metadata = new Dictionary<string, string>
            {
                ["flow_type"] = "coin_package",
                ["user_id"] = request.UserId.ToString(),
                ["coin_package_id"] = package.Id.ToString(),
                ["transaction_id"] = transaction.Id.ToString()
            };

            var stripeResult = await _stripePaymentService.CreateCoinPackagePaymentIntentAsync(
                customerResult.Value.StripeCustomerId,
                customerResult.Value.User.Email,
                customerResult.Value.User.FullName ?? customerResult.Value.User.Username,
                package.Price,
                _configuredCurrency,
                package.Name,
                metadata,
                defaultPaymentMethodId,
                cancellationToken);

            transaction.ProviderReferenceId = stripeResult.PaymentIntentId;
            transaction.Status = stripeResult.Status;
            transaction.Cost = stripeResult.AmountDue;
            transaction.UpdatedAt = now;

            if (string.Equals(stripeResult.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var confirmResult = await _sender.Send(
                    new ConfirmCoinPackagePaymentCommand(
                        request.UserId,
                        package.Id,
                        transaction.Id,
                        stripeResult.PaymentIntentId,
                        stripeResult.Status),
                    cancellationToken);

                if (confirmResult.IsFailure)
                {
                    return Result.Failure<CoinPackageCheckoutResponse>(confirmResult.Error);
                }
            }

            return Result.Success(new CoinPackageCheckoutResponse(
                package.Id,
                transaction.Id,
                stripeResult.PaymentIntentId,
                stripeResult.ClientSecret,
                stripeResult.Status,
                stripeResult.AmountDue,
                stripeResult.Currency));
        }
        catch (Exception ex)
        {
            return Result.Failure<CoinPackageCheckoutResponse>(
                new Error("Stripe.PaymentFailed", ex.Message));
        }
    }

    private static string ResolveCurrency(BillingCurrencyOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Currency)
            ? "vnd"
            : options.Currency.Trim().ToLowerInvariant();
    }
}
