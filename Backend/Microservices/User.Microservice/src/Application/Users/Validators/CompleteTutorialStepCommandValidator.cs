using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class CompleteTutorialStepCommandValidator : AbstractValidator<CompleteTutorialStepCommand>
{
    public CompleteTutorialStepCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Step)
            .InclusiveBetween(1, 2)
            .WithMessage("Tutorial step must be 1 or 2");
    }
}
