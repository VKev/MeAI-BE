using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class DeleteCoinPackageCommandValidator : AbstractValidator<DeleteCoinPackageCommand>
{
    public DeleteCoinPackageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
