using Application.Abstractions.Billing;
using Application.Abstractions.Data;
using Application.Billing.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Billing.Commands;

public sealed record ConfirmCoinPackagePaymentCommand(
    Guid UserId,
    Guid? PackageId,
    Guid? TransactionId,
    string PaymentIntentId,
    string Status) : IRequest<Result<ConfirmCoinPackagePaymentResponse>>;

public sealed class ConfirmCoinPackagePaymentCommandHandler
    : IRequestHandler<ConfirmCoinPackagePaymentCommand, Result<ConfirmCoinPackagePaymentResponse>>
{
    private readonly IRepository<CoinPackage> _coinPackageRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IBillingService _billingService;

    public ConfirmCoinPackagePaymentCommandHandler(
        IUnitOfWork unitOfWork,
        IBillingService billingService)
    {
        _coinPackageRepository = unitOfWork.Repository<CoinPackage>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _billingService = billingService;
    }

    public async Task<Result<ConfirmCoinPackagePaymentResponse>> Handle(
        ConfirmCoinPackagePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatus(request.Status);
        var transactionQuery = _transactionRepository.GetAll()
            .Where(item =>
                item.UserId == request.UserId &&
                item.RelationType == "CoinPackage" &&
                item.PaymentMethod == "Stripe" &&
                !item.IsDeleted);

        Transaction? transaction = null;
        if (request.TransactionId.HasValue)
        {
            var trackedTransaction = await _transactionRepository.GetByIdAsync(request.TransactionId.Value, cancellationToken);
            if (trackedTransaction != null &&
                trackedTransaction.UserId == request.UserId &&
                trackedTransaction.RelationType == "CoinPackage" &&
                !trackedTransaction.IsDeleted)
            {
                transaction = trackedTransaction;
            }
        }

        transaction ??= await transactionQuery
            .FirstOrDefaultAsync(item => item.ProviderReferenceId == request.PaymentIntentId, cancellationToken);

        if (transaction == null)
        {
            return Result.Failure<ConfirmCoinPackagePaymentResponse>(
                new Error("CoinPackage.TransactionNotFound", "Coin package transaction was not found."));
        }

        if (!string.IsNullOrWhiteSpace(transaction.ProviderReferenceId) &&
            !string.Equals(transaction.ProviderReferenceId, request.PaymentIntentId, StringComparison.Ordinal))
        {
            return Result.Failure<ConfirmCoinPackagePaymentResponse>(
                new Error("CoinPackage.PaymentIntentMismatch", "Payment intent does not match the transaction."));
        }

        if (!transaction.RelationId.HasValue)
        {
            return Result.Failure<ConfirmCoinPackagePaymentResponse>(
                new Error("CoinPackage.PackageMissing", "Transaction is missing its coin package reference."));
        }

        if (request.PackageId.HasValue && request.PackageId.Value != transaction.RelationId.Value)
        {
            return Result.Failure<ConfirmCoinPackagePaymentResponse>(
                new Error("CoinPackage.PackageMismatch", "Coin package does not match the transaction."));
        }

        var package = await _coinPackageRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == transaction.RelationId.Value, cancellationToken);

        if (package == null)
        {
            return Result.Failure<ConfirmCoinPackagePaymentResponse>(
                new Error("CoinPackage.NotFound", "Coin package not found."));
        }

        transaction.Status = normalizedStatus;
        transaction.ProviderReferenceId = request.PaymentIntentId;
        transaction.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _transactionRepository.Update(transaction);

        if (!IsSuccessfulStatus(normalizedStatus))
        {
            return Result.Success(new ConfirmCoinPackagePaymentResponse(
                package.Id,
                transaction.Id,
                normalizedStatus,
                false,
                false,
                package.CoinAmount + package.BonusCoins,
                0m));
        }

        var creditedAmount = package.CoinAmount + package.BonusCoins;
        var refundResult = await _billingService.RefundAsync(
            request.UserId,
            creditedAmount,
            "coin_package.purchase",
            "coin_package",
            transaction.Id.ToString(),
            cancellationToken);

        if (refundResult.IsFailure)
        {
            return Result.Failure<ConfirmCoinPackagePaymentResponse>(refundResult.Error);
        }

        return Result.Success(new ConfirmCoinPackagePaymentResponse(
            package.Id,
            transaction.Id,
            normalizedStatus,
            !refundResult.Value.AlreadyApplied,
            refundResult.Value.AlreadyApplied,
            creditedAmount,
            refundResult.Value.NewBalance));
    }

    private static string NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "pending";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "paid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "complete", StringComparison.OrdinalIgnoreCase))
        {
            return "succeeded";
        }

        if (string.Equals(normalized, "canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        return normalized;
    }

    private static bool IsSuccessfulStatus(string? value) =>
        string.Equals(value?.Trim(), "succeeded", StringComparison.OrdinalIgnoreCase);
}
