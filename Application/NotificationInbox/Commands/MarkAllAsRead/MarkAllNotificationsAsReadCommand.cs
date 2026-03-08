using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.MarkAllAsRead;

public record MarkAllNotificationsAsReadCommand : IRequest<int>;

public class MarkAllNotificationsAsReadCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<MarkAllNotificationsAsReadCommand, int>
{
    public async Task<int> Handle(MarkAllNotificationsAsReadCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");

        var unreadNotifications = await applicationDbContext.NotificationInboxes
            .Where(x => x.UserId == user.Id && !x.IsRead && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
            return 0;

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
        }

        await applicationDbContext.SaveChanges(cancellationToken);

        return unreadNotifications.Count;
    }
}
