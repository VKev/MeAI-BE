using Application.Abstractions.Data;
using Application.Common;
using Application.Reports.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Reports.Commands;

public sealed record CreateReportCommand(
    Guid ReporterId,
    string TargetType,
    Guid TargetId,
    string Reason) : ICommand<ReportResponse>;

public sealed class CreateReportCommandHandler : ICommandHandler<CreateReportCommand, ReportResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateReportCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReportResponse>> Handle(CreateReportCommand request, CancellationToken cancellationToken)
    {
        var targetType = FeedPostSupport.NormalizeOptionalText(request.TargetType);
        var reason = FeedPostSupport.NormalizeOptionalText(request.Reason);

        if (reason is null)
        {
            return Result.Failure<ReportResponse>(FeedErrors.InvalidReportReason());
        }

        if (!string.Equals(targetType, "Post", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetType, "Comment", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ReportResponse>(FeedErrors.InvalidReportTarget);
        }

        if (string.Equals(targetType, "Post", StringComparison.OrdinalIgnoreCase))
        {
            var postExists = await _unitOfWork.Repository<Post>()
                .GetAll()
                .AnyAsync(item => item.Id == request.TargetId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

            if (!postExists)
            {
                return Result.Failure<ReportResponse>(FeedErrors.PostNotFound);
            }
        }
        else
        {
            var commentExists = await _unitOfWork.Repository<Comment>()
                .GetAll()
                .AnyAsync(item => item.Id == request.TargetId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

            if (!commentExists)
            {
                return Result.Failure<ReportResponse>(FeedErrors.CommentNotFound);
            }
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var report = new Report
        {
            Id = Guid.CreateVersion7(),
            ReporterId = request.ReporterId,
            TargetType = targetType!,
            TargetId = request.TargetId,
            Reason = reason,
            Status = "Pending",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _unitOfWork.Repository<Report>().AddAsync(report, cancellationToken);
        return Result.Success(ReportResponseMapping.ToResponse(report));
    }
}
