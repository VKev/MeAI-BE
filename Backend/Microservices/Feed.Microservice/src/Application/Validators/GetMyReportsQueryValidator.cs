using Application.Reports.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetMyReportsQueryValidator : AbstractValidator<GetMyReportsQuery>
{
    public GetMyReportsQueryValidator()
    {
        RuleFor(query => query.ReporterId).NotEmpty();
        RuleFor(query => query.Status)
            .MaximumLength(50)
            .When(query => !string.IsNullOrWhiteSpace(query.Status));
        RuleFor(query => query.TargetType)
            .MaximumLength(50)
            .When(query => !string.IsNullOrWhiteSpace(query.TargetType));
    }
}
