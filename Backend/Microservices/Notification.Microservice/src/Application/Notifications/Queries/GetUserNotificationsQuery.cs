using Application.Notifications.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentValidation;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Notifications.Queries;

public sealed record GetUserNotificationsQuery(Guid UserId, bool OnlyUnread, int Limit)
    : IQuery<IReadOnlyList<NotificationDeliveryModel>>;

public sealed class GetUserNotificationsQueryValidator : AbstractValidator<GetUserNotificationsQuery>
{
    public GetUserNotificationsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

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
        var userNotifications = await _userNotificationRepository.GetByUserIdAsync(
            request.UserId,
            request.OnlyUnread,
            request.Limit,
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
