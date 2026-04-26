using Domain.Entities;
using Application.Comments.Models;
using Application.Posts.Models;

namespace Application.Reports.Models;

public sealed record ReportResponse(
    Guid Id,
    Guid ReporterId,
    string TargetType,
    Guid TargetId,
    string Reason,
    string Status,
    Guid? ReviewedByAdminId,
    DateTime? ReviewedAt,
    string? ResolutionNote,
    string? ActionType,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record AdminReportCommentPreviewResponse(
    CommentResponse TargetComment,
    CommentResponse? ParentComment,
    IReadOnlyList<CommentResponse> Comments);

public sealed record AdminReportPreviewResponse(
    ReportResponse Report,
    PostResponse? Post,
    AdminReportCommentPreviewResponse? Comment);

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
            report.ReviewedByAdminId,
            report.ReviewedAt,
            report.ResolutionNote,
            report.ActionType,
            report.CreatedAt,
            report.UpdatedAt);
    }
}
