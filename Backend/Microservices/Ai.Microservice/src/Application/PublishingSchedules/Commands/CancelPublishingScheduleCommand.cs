using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record CancelPublishingScheduleCommand(
    Guid ScheduleId,
    Guid UserId) : IRequest<Result<bool>>;

public sealed class CancelPublishingScheduleCommandHandler
    : IRequestHandler<CancelPublishingScheduleCommand, Result<bool>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IPostRepository _postRepository;

    public CancelPublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IPostRepository postRepository)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _postRepository = postRepository;
    }

    public async Task<Result<bool>> Handle(
        CancelPublishingScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(request.ScheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return Result.Failure<bool>(PublishingScheduleErrors.NotFound);
        }

        if (schedule.UserId != request.UserId)
        {
            return Result.Failure<bool>(PublishingScheduleErrors.Unauthorized);
        }

        if (string.Equals(schedule.Status, PublishingScheduleState.StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<bool>(PublishingScheduleErrors.ScheduleAlreadyCancelled);
        }

        var postIds = schedule.Items
            .Where(item => !item.DeletedAt.HasValue && string.Equals(item.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ItemId)
            .Distinct()
            .ToList();

        var posts = postIds.Count == 0
            ? []
            : await _postRepository.GetByIdsForUpdateAsync(postIds, cancellationToken);

        PublishingScheduleCommandSupport.ClearPostScheduling(schedule.Id, posts);

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        schedule.Status = PublishingScheduleState.StatusCancelled;
        schedule.UpdatedAt = now;
        schedule.ErrorCode = null;
        schedule.ErrorMessage = null;
        schedule.NextRetryAt = null;

        foreach (var item in schedule.Items.Where(item => !item.DeletedAt.HasValue))
        {
            item.Status = PublishingScheduleState.ItemStatusCancelled;
            item.UpdatedAt = now;
            item.ErrorMessage = null;
        }

        foreach (var post in posts)
        {
            _postRepository.Update(post);
        }

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
