using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Billing;

public static class BillingClientErrors
{
    public const string InsufficientFunds = "Billing.InsufficientFunds";
    public const string UserNotFound = "Billing.UserNotFound";
    public const string InvalidAmount = "Billing.InvalidAmount";
}

public interface IBillingClient
{
    Task<Result<decimal>> GetBalanceAsync(Guid userId, CancellationToken cancellationToken);

    // Debits `amount` coins. Error code `BillingClientErrors.InsufficientFunds` when the
    // user can't afford it — callers bubble this up so the FE can pop the top-up CTA.
    Task<Result<decimal>> DebitAsync(
        Guid userId,
        decimal amount,
        string reason,
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken);

    // Always returns Success when the refund was applied OR was already applied previously
    // (idempotent on reason+referenceType+referenceId).
    Task<Result<decimal>> RefundAsync(
        Guid userId,
        decimal amount,
        string reason,
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken);
}
