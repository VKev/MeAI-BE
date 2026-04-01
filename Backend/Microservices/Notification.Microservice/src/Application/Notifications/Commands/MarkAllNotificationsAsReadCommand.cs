using Application.Abstractions.Data;
using Application.Notifications.Models;
using Domain.Repositories;
using FluentValidation;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Notifications.Commands;

public sealed record MarkAllNotificationsAsReadCommand(Guid UserId)
    : ICommand<MarkAllNotificationsAsReadResponse>;

public sealed class MarkAllNotificationsAsReadCommandValidator : AbstractValidator<MarkAllNotificationsAsReadCommand>
{
    public MarkAllNotificationsAsReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class MarkAllNotificationsAsReadCommandHandler
    : ICommandHandler<MarkAllNotificationsAsReadCommand, MarkAllNotificationsAsReadResponse>
{
    private readonly IUserNotificationRepository _userNotificationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public MarkAllNotificationsAsReadCommandHandler(
        IUserNotificationRepository userNotificationRepository,
        IUnitOfWork unitOfWork)
    {
        _userNotificationRepository = userNotificationRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<MarkAllNotificationsAsReadResponse>> Handle(
        MarkAllNotificationsAsReadCommand request,
        CancellationToken cancellationToken)
    {
        var unreadNotifications = await _userNotificationRepository.GetUnreadTrackedByUserIdAsync(
            request.UserId,
            cancellationToken);

        var markedAt = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var userNotification in unreadNotifications)
        {
            userNotification.IsRead = true;
            userNotification.ReadAt = markedAt;
            userNotification.UpdatedAt = markedAt;
        }

        if (unreadNotifications.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new MarkAllNotificationsAsReadResponse(unreadNotifications.Count, markedAt));
    }
}
