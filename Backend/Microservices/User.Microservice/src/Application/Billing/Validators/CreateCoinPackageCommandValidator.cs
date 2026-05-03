using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class CreateCoinPackageCommandValidator : AbstractValidator<CreateCoinPackageCommand>
{
    public CreateCoinPackageCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.CoinAmount).GreaterThan(0m);
        RuleFor(x => x.BonusCoins).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Price).GreaterThan(0m);
        RuleFor(x => x.Currency).NotEmpty().Must(v => string.Equals(v, "usd", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
