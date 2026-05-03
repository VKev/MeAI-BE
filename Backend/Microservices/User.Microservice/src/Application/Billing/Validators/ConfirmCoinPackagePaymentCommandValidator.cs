using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class ConfirmCoinPackagePaymentCommandValidator : AbstractValidator<ConfirmCoinPackagePaymentCommand>
{
    public ConfirmCoinPackagePaymentCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PaymentIntentId).NotEmpty();
        RuleFor(x => x.Status).NotEmpty();
    }
}
