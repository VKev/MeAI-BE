using Application.Notifications.Models;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Notifications.Queries;

public sealed record GetUserNotificationsQuery(
    Guid UserId,
    bool OnlyUnread,
    int Limit,
    string? Source,
    string? TypePrefix,
    string? RelatedId,
    DateTime? BeforeCreatedAt = null)
    : IQuery<IReadOnlyList<NotificationDeliveryModel>>;

public sealed class GetUserNotificationsQueryHandler
    : IQueryHandler<GetUserNotificationsQuery, IReadOnlyList<NotificationDeliveryModel>>
{
    private readonly IUserNotificationRepository _userNotificationRepository;

    public GetUserNotificationsQueryHandler(IUserNotificationRepository userNotificationRepository)
    {
        _userNotificationRepository = userNotificationRepository;
    }

    public async Task<Result<IReadOnlyList<NotificationDeliveryModel>>> Handle(
        GetUserNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);
        var userNotifications = await _userNotificationRepository.GetByUserIdAsync(
            request.UserId,
            request.OnlyUnread,
            limit,
            request.Source,
            request.TypePrefix,
            request.RelatedId,
            request.BeforeCreatedAt,
            cancellationToken);

        var response = userNotifications
            .Select(MapToDeliveryModel)
            .ToList();

        return Result.Success<IReadOnlyList<NotificationDeliveryModel>>(response);
    }

    internal static NotificationDeliveryModel MapToDeliveryModel(UserNotification userNotification)
    {
        return new NotificationDeliveryModel(
            userNotification.NotificationId,
            userNotification.Id,
            userNotification.UserId,
            userNotification.Notification.Source,
            userNotification.Notification.Type,
            userNotification.Notification.Title,
            userNotification.Notification.Message,
            userNotification.Notification.PayloadJson,
            userNotification.Notification.CreatedByUserId,
            userNotification.IsRead,
            userNotification.ReadAt,
            userNotification.WasOnlineWhenCreated,
            userNotification.CreatedAt,
            userNotification.UpdatedAt);
    }
}
