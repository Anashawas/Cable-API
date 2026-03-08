using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.DeleteNotification;

public record DeleteNotificationCommand(int NotificationId) : IRequest;

public class DeleteNotificationCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteNotificationCommand>
{
    public async Task Handle(DeleteNotificationCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");

        var notification = await applicationDbContext.NotificationInboxes
            .FirstOrDefaultAsync(
                x => x.Id == request.NotificationId &&
                     x.UserId == user.Id &&
                     !x.IsDeleted,
                cancellationToken);

        if (notification == null)
            throw new NotFoundException($"Notification with id {request.NotificationId} not found");

        notification.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
