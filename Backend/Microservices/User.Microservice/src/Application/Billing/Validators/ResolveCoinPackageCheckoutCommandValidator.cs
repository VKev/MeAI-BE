using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class ResolveCoinPackageCheckoutCommandValidator : AbstractValidator<ResolveCoinPackageCheckoutCommand>
{
    public ResolveCoinPackageCheckoutCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PaymentIntentId).NotEmpty();
    }
}
