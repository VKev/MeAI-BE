namespace Application.Abstractions.Payments;

public interface IStripePaymentService
{
    Task<StripeCustomerResult> CreateCustomerAsync(
        string? customerEmail,
        string? customerName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeCatalogPriceResult> EnsureRecurringPriceAsync(
        string? stripeProductId,
        string? stripePriceId,
        decimal amount,
        int durationMonths,
        string? subscriptionName,
        CancellationToken cancellationToken = default);

    Task<StripeRecurringSubscriptionResult> CreateSubscriptionAsync(
        string stripePriceId,
        string? stripeCustomerId,
        string? customerEmail,
        string? customerName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeOneTimePaymentResult> CreateCoinPackagePaymentIntentAsync(
        string stripeCustomerId,
        string? customerEmail,
        string? customerName,
        decimal amount,
        string currency,
        string description,
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

    Task<StripeCheckoutStatusResult> GetCoinPackageCheckoutStatusAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default);

    Task<StripeSubscriptionSnapshotResult> GetSubscriptionSnapshotAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default);

    Task UpdateSubscriptionMetadataAsync(
        string stripeSubscriptionId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeAutoRenewUpdateResult> SetSubscriptionAutoRenewAsync(
        string stripeSubscriptionId,
        string? stripeScheduleId,
        bool enabled,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StripeCardResult>> GetCustomerCardsAsync(
        string stripeCustomerId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default);

    Task<StripeCardSetupIntentResult> CreateCardSetupIntentAsync(
        string stripeCustomerId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeCardResult> SetDefaultCardAsync(
        string stripeCustomerId,
        string paymentMethodId,
        IEnumerable<string> stripeSubscriptionIds,
        CancellationToken cancellationToken = default);
}

public sealed record StripeCustomerResult(string StripeCustomerId);

public sealed record StripeRecurringSubscriptionResult(
    string StripeSubscriptionId,
    string StripeCustomerId,
    string Status,
    string? PaymentIntentId,
    string? ClientSecret,
    string Currency,
    decimal AmountDue,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd);

public sealed record StripeOneTimePaymentResult(
    string PaymentIntentId,
    string ClientSecret,
    string Status,
    string Currency,
    decimal AmountDue);

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
    string StripeCustomerId,
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

public sealed record StripeAutoRenewUpdateResult(
    string StripeSubscriptionId,
    string Status,
    string? StripeScheduleId,
    DateTime? CurrentPeriodEnd);

public sealed record StripeCardResult(
    string PaymentMethodId,
    string? Brand,
    string? Last4,
    long? ExpMonth,
    long? ExpYear,
    string? Funding,
    string? Country,
    string? CardholderName,
    bool IsDefault,
    bool IsExpired);

public sealed record StripeCardSetupIntentResult(
    string SetupIntentId,
    string ClientSecret,
    string StripeCustomerId);
