using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Notifications.Models;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Services;

public sealed class NotificationDispatchService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUserNotificationRepository _userNotificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationPresenceService _notificationPresenceService;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(
        INotificationRepository notificationRepository,
        IUserNotificationRepository userNotificationRepository,
        IUnitOfWork unitOfWork,
        INotificationPresenceService notificationPresenceService,
        INotificationRealtimeNotifier notificationRealtimeNotifier,
        ILogger<NotificationDispatchService> logger)
    {
        _notificationRepository = notificationRepository;
        _userNotificationRepository = userNotificationRepository;
        _unitOfWork = unitOfWork;
        _notificationPresenceService = notificationPresenceService;
        _notificationRealtimeNotifier = notificationRealtimeNotifier;
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationRequestedEvent message, CancellationToken cancellationToken)
    {
        if (message.RecipientUserIds.Count == 0)
        {
            _logger.LogWarning("Notification event skipped because it has no recipients. Type: {Type}", message.Type);
            return;
        }

        var notificationId = message.NotificationId == Guid.Empty
            ? Guid.CreateVersion7()
            : message.NotificationId;

        var notificationAlreadyExists = await _notificationRepository.ExistsAsync(notificationId, cancellationToken);
        if (notificationAlreadyExists)
        {
            _logger.LogInformation(
                "Notification event ignored because notification already exists. NotificationId: {NotificationId}",
                notificationId);
            return;
        }

        var createdAt = message.CreatedAt == default
            ? DateTimeExtensions.PostgreSqlUtcNow
            : message.CreatedAt;

        var notification = new Notification
        {
            Id = notificationId,
            Type = message.Type,
            Title = message.Title,
            Message = message.Message,
            PayloadJson = message.PayloadJson,
            CreatedByUserId = message.CreatedByUserId,
            CreatedAt = createdAt
        };

        await _notificationRepository.AddAsync(notification, cancellationToken);

        var onlineStates = new Dictionary<Guid, bool>();
        foreach (var userId in message.RecipientUserIds.Distinct())
        {
            onlineStates[userId] = await _notificationPresenceService.IsUserOnlineAsync(userId, cancellationToken);
        }

        var userNotifications = message.RecipientUserIds
            .Distinct()
            .Select(userId => new UserNotification
            {
                Id = Guid.CreateVersion7(),
                NotificationId = notification.Id,
                UserId = userId,
                IsRead = false,
                ReadAt = null,
                WasOnlineWhenCreated = onlineStates[userId],
                CreatedAt = createdAt
            })
            .ToList();

        await _userNotificationRepository.AddRangeAsync(userNotifications, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var userNotification in userNotifications.Where(item => item.WasOnlineWhenCreated))
        {
            var delivery = new NotificationDeliveryModel(
                notification.Id,
                userNotification.Id,
                userNotification.UserId,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.PayloadJson,
                notification.CreatedByUserId,
                userNotification.IsRead,
                userNotification.ReadAt,
                userNotification.WasOnlineWhenCreated,
                userNotification.CreatedAt,
                userNotification.UpdatedAt);

            await _notificationRealtimeNotifier.NotifyUserAsync(delivery, cancellationToken);
        }

        _logger.LogInformation(
            "Notification dispatched. NotificationId: {NotificationId}, Recipients: {RecipientCount}, OnlineRecipients: {OnlineRecipientCount}",
            notification.Id,
            userNotifications.Count,
            userNotifications.Count(item => item.WasOnlineWhenCreated));
    }
}
