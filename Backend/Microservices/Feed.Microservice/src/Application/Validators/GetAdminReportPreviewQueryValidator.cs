using Application.Reports.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetAdminReportPreviewQueryValidator : AbstractValidator<GetAdminReportPreviewQuery>
{
    public GetAdminReportPreviewQueryValidator()
    {
        RuleFor(query => query.ReportId)
            .NotEmpty();

        RuleFor(query => query.RequestingUserId)
            .NotEmpty();

        RuleFor(query => query)
            .Must(HaveValidCursorPair)
            .WithMessage("cursorCreatedAt and cursorId must be provided together.");
    }

    private static bool HaveValidCursorPair(GetAdminReportPreviewQuery query)
    {
        return query.CursorCreatedAt.HasValue == query.CursorId.HasValue;
    }
}
