using Application.Billing.Commands;
using FluentValidation;
using Microsoft.Extensions.Options;
using SharedLibrary.Configs;

namespace Application.Billing.Validators;

public sealed class UpdateCoinPackageCommandValidator : AbstractValidator<UpdateCoinPackageCommand>
{
    public UpdateCoinPackageCommandValidator(IOptions<BillingCurrencyOptions> billingCurrencyOptions)
    {
        var configuredCurrency = ResolveCurrency(billingCurrencyOptions.Value);

        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.CoinAmount).GreaterThan(0m);
        RuleFor(x => x.BonusCoins).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Price).GreaterThan(0m);
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(v => string.Equals(v, configuredCurrency, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"Currency must match configured Stripe currency '{configuredCurrency}'.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }

    private static string ResolveCurrency(BillingCurrencyOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Currency)
            ? "vnd"
            : options.Currency.Trim().ToLowerInvariant();
    }
}
