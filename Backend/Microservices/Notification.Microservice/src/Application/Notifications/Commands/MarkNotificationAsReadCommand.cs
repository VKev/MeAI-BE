using Application.Abstractions.Data;
using Application.Notifications.Models;
using Application.Notifications.Queries;
using Domain.Repositories;
using FluentValidation;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Notifications.Commands;

public sealed record MarkNotificationAsReadCommand(Guid UserId, Guid UserNotificationId)
    : ICommand<NotificationDeliveryModel>;

public sealed class MarkNotificationAsReadCommandValidator : AbstractValidator<MarkNotificationAsReadCommand>
{
    public MarkNotificationAsReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.UserNotificationId).NotEmpty();
    }
}

public sealed class MarkNotificationAsReadCommandHandler
    : ICommandHandler<MarkNotificationAsReadCommand, NotificationDeliveryModel>
{
    private static readonly Error UserNotificationNotFound = new(
        "Notification.UserNotificationNotFound",
        "Notification does not exist for the current user.");

    private readonly IUserNotificationRepository _userNotificationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public MarkNotificationAsReadCommandHandler(
        IUserNotificationRepository userNotificationRepository,
        IUnitOfWork unitOfWork)
    {
        _userNotificationRepository = userNotificationRepository;
        _unitOfWork = unitOfWork;
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

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(GetUserNotificationsQueryHandler.MapToDeliveryModel(userNotification));
    }
}
