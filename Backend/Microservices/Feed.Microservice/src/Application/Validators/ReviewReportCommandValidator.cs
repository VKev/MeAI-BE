using Application.Reports.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class ReviewReportCommandValidator : AbstractValidator<ReviewReportCommand>
{
    public ReviewReportCommandValidator()
    {
        RuleFor(command => command.AdminUserId).NotEmpty();
        RuleFor(command => command.ReportId).NotEmpty();
        RuleFor(command => command.Status)
            .NotEmpty()
            .MaximumLength(50);
        RuleFor(command => command.Action)
            .MaximumLength(100)
            .When(command => !string.IsNullOrWhiteSpace(command.Action));
        RuleFor(command => command.ResolutionNote)
            .MaximumLength(2000)
            .When(command => !string.IsNullOrWhiteSpace(command.ResolutionNote));
    }
}
