namespace Application.Billing.Models;

public sealed record CoinPackageResponse(
    Guid Id,
    string Name,
    decimal CoinAmount,
    decimal BonusCoins,
    decimal TotalCoins,
    decimal Price,
    string Currency,
    int DisplayOrder);

public sealed record AdminCoinPackageResponse(
    Guid Id,
    string Name,
    decimal CoinAmount,
    decimal BonusCoins,
    decimal TotalCoins,
    decimal Price,
    string Currency,
    bool IsActive,
    int DisplayOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CoinPackageCheckoutResponse(
    Guid PackageId,
    Guid TransactionId,
    string PaymentIntentId,
    string ClientSecret,
    string Status,
    decimal AmountDue,
    string Currency);

public sealed record ConfirmCoinPackagePaymentResponse(
    Guid PackageId,
    Guid TransactionId,
    string Status,
    bool CoinsCredited,
    bool AlreadyCredited,
    decimal CreditedCoins,
    decimal CurrentBalance);

public sealed record ResolveCoinPackageCheckoutResponse(
    Guid PackageId,
    Guid TransactionId,
    string PaymentIntentId,
    string Status,
    bool IsFinal,
    bool CoinsCredited,
    bool AlreadyCredited,
    decimal CreditedCoins,
    decimal CurrentBalance);
