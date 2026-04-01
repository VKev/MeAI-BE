using Application.Notifications.Models;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Notifications.Commands;

public sealed record MarkAllNotificationsAsReadCommand(Guid UserId)
    : ICommand<MarkAllNotificationsAsReadResponse>;

public sealed class MarkAllNotificationsAsReadCommandHandler
    : ICommandHandler<MarkAllNotificationsAsReadCommand, MarkAllNotificationsAsReadResponse>
{
    private readonly IUserNotificationRepository _userNotificationRepository;

    public MarkAllNotificationsAsReadCommandHandler(IUserNotificationRepository userNotificationRepository)
    {
        _userNotificationRepository = userNotificationRepository;
    }

    public async Task<Result<MarkAllNotificationsAsReadResponse>> Handle(
        MarkAllNotificationsAsReadCommand request,
        CancellationToken cancellationToken)
    {
        var unreadNotifications = await _userNotificationRepository.GetUnreadTrackedByUserIdAsync(
            request.UserId,
            cancellationToken);

        var markedAt = DateTimeExtensions.PostgreSqlUtcNow;
        if (unreadNotifications.Count > 0)
        {
            foreach (var userNotification in unreadNotifications)
            {
                userNotification.IsRead = true;
                userNotification.ReadAt = markedAt;
                userNotification.UpdatedAt = markedAt;
            }
        }

        return Result.Success(new MarkAllNotificationsAsReadResponse(unreadNotifications.Count, markedAt));
    }
}
