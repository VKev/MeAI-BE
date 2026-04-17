using Domain.Entities;

namespace Application.Reports.Models;

public sealed record ReportResponse(
    Guid Id,
    Guid ReporterId,
    string TargetType,
    Guid TargetId,
    string Reason,
    string Status,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

internal static class ReportResponseMapping
{
    public static ReportResponse ToResponse(Report report)
    {
        return new ReportResponse(
            report.Id,
            report.ReporterId,
            report.TargetType,
            report.TargetId,
            report.Reason,
            report.Status,
            report.CreatedAt,
            report.UpdatedAt);
    }
}
