using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.Notifications;

namespace Infrastructure.Logic.Consumers;

public sealed class NotificationRequestedConsumer : IConsumer<NotificationRequestedEvent>
{
    private readonly NotificationDispatchService _notificationDispatchService;
    private readonly ILogger<NotificationRequestedConsumer> _logger;

    public NotificationRequestedConsumer(
        NotificationDispatchService notificationDispatchService,
        ILogger<NotificationRequestedConsumer> logger)
    {
        _notificationDispatchService = notificationDispatchService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NotificationRequestedEvent> context)
    {
        _logger.LogInformation(
            "Notification event received. NotificationId: {NotificationId}, Type: {Type}, Recipients: {RecipientCount}",
            context.Message.NotificationId,
            context.Message.Type,
            context.Message.RecipientUserIds.Count);

        await _notificationDispatchService.DispatchAsync(context.Message, context.CancellationToken);
    }
}
