using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Billing;

// Billing error codes exposed to cross-service callers. The FE matches on these to decide
// UX (e.g. InsufficientFunds → show the top-up CTA).
public static class BillingErrors
{
    public const string InsufficientFunds = "Billing.InsufficientFunds";
    public const string UserNotFound = "Billing.UserNotFound";
    public const string InvalidAmount = "Billing.InvalidAmount";
}

public interface IBillingService
{
    Task<Result<decimal>> GetBalanceAsync(Guid userId, CancellationToken cancellationToken);

    // Deducts `amount` from the user with a row-level lock so concurrent debits can't
    // leave the balance negative. Writes a CoinTransaction ledger entry in the same
    // transaction so ledger + balance never drift.
    Task<Result<decimal>> DebitAsync(
        Guid userId,
        decimal amount,
        string reason,
        string? referenceType,
        string? referenceId,
        CancellationToken cancellationToken);

    // Credits `amount` back. Idempotent on the (reason, referenceType, referenceId) key —
    // a second call for the same refund returns AlreadyApplied=true without re-crediting.
    Task<Result<RefundResult>> RefundAsync(
        Guid userId,
        decimal amount,
        string reason,
        string? referenceType,
        string? referenceId,
        CancellationToken cancellationToken);
}

public sealed record RefundResult(decimal NewBalance, bool AlreadyApplied);
