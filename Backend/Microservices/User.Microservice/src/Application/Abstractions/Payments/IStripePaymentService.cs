namespace Application.Abstractions.Payments;

public interface IStripePaymentService
{
    Task<StripeCatalogPriceResult> EnsureRecurringPriceAsync(
        string? stripeProductId,
        string? stripePriceId,
        decimal amount,
        int durationMonths,
        string? subscriptionName,
        CancellationToken cancellationToken = default);

    Task<StripeRecurringSubscriptionResult> CreateSubscriptionAsync(
        string stripePriceId,
        string? customerEmail,
        string? customerName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeRecurringSubscriptionResult> UpgradeSubscriptionAsync(
        string stripeSubscriptionId,
        string stripePriceId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeScheduledChangeResult> ScheduleSubscriptionChangeAsync(
        string stripeSubscriptionId,
        string currentStripePriceId,
        string nextStripePriceId,
        IDictionary<string, string> currentMetadata,
        IDictionary<string, string> nextPhaseMetadata,
        CancellationToken cancellationToken = default);

    Task<StripeCheckoutStatusResult> GetCheckoutStatusAsync(
        string? paymentIntentId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default);

    Task<StripeSubscriptionSnapshotResult> GetSubscriptionSnapshotAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default);

    Task UpdateSubscriptionMetadataAsync(
        string stripeSubscriptionId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);
}

public sealed record StripeRecurringSubscriptionResult(
    string StripeSubscriptionId,
    string Status,
    string? PaymentIntentId,
    string? ClientSecret,
    string Currency,
    decimal AmountDue,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd);

public sealed record StripeCatalogPriceResult(
    string StripeProductId,
    string StripePriceId);

public sealed record StripeScheduledChangeResult(
    string StripeSubscriptionId,
    string StripeScheduleId,
    string Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd);

public sealed record StripeSubscriptionSnapshotResult(
    string StripeSubscriptionId,
    string Status,
    string? LatestInvoiceId,
    string? ClientSecret,
    string? PaymentIntentId,
    string? StripeScheduleId,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd);

public sealed record StripeCheckoutStatusResult(
    string Status,
    bool IsSuccessful,
    bool IsTerminal,
    string? ProviderReferenceId,
    string? PaymentIntentId,
    string? StripeSubscriptionId);
