using Application.Reports.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class CreateReportCommandValidator : AbstractValidator<CreateReportCommand>
{
    public CreateReportCommandValidator()
    {
        RuleFor(command => command.ReporterId).NotEmpty();
        RuleFor(command => command.TargetId).NotEmpty();
        RuleFor(command => command.TargetType)
            .NotEmpty()
            .MaximumLength(50);
        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(2000);
    }
}
