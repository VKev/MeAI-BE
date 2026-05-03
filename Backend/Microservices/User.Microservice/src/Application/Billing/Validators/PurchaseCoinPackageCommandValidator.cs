using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class PurchaseCoinPackageCommandValidator : AbstractValidator<PurchaseCoinPackageCommand>
{
    public PurchaseCoinPackageCommandValidator()
    {
        RuleFor(x => x.PackageId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
