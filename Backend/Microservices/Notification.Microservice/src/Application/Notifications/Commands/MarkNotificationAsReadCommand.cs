using Application.Notifications.Models;
using Application.Notifications.Queries;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Notifications.Commands;

public sealed record MarkNotificationAsReadCommand(Guid UserId, Guid UserNotificationId)
    : ICommand<NotificationDeliveryModel>;

public sealed class MarkNotificationAsReadCommandHandler
    : ICommandHandler<MarkNotificationAsReadCommand, NotificationDeliveryModel>
{
    private static readonly Error UserNotificationNotFound = new(
        "Notification.UserNotificationNotFound",
        "Notification does not exist for the current user.");

    private readonly IUserNotificationRepository _userNotificationRepository;

    public MarkNotificationAsReadCommandHandler(IUserNotificationRepository userNotificationRepository)
    {
        _userNotificationRepository = userNotificationRepository;
    }

    public async Task<Result<NotificationDeliveryModel>> Handle(
        MarkNotificationAsReadCommand request,
        CancellationToken cancellationToken)
    {
        var userNotification = await _userNotificationRepository.GetTrackedByIdAndUserIdAsync(
            request.UserNotificationId,
            request.UserId,
            cancellationToken);

        if (userNotification is null)
        {
            return Result.Failure<NotificationDeliveryModel>(UserNotificationNotFound);
        }

        if (!userNotification.IsRead)
        {
            var readAt = DateTimeExtensions.PostgreSqlUtcNow;
            userNotification.IsRead = true;
            userNotification.ReadAt = readAt;
            userNotification.UpdatedAt = readAt;
        }

        return Result.Success(GetUserNotificationsQueryHandler.MapToDeliveryModel(userNotification));
    }
}
