using Application.Common.Interfaces;
using Application.NotificationInbox.Queries.GetUserNotifications;
using Cable.Core;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetNotificationById;

public record GetNotificationByIdRequest(int NotificationId) : IRequest<NotificationDto>;

public class GetNotificationByIdQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetNotificationByIdRequest, NotificationDto>
{
    public async Task<NotificationDto> Handle(
        GetNotificationByIdRequest request,
        CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");

        var notification = await applicationDbContext.NotificationInboxes
            .AsNoTracking()
            .Where(x => x.Id == request.NotificationId && x.UserId == user.Id && !x.IsDeleted)
            .Include(x => x.NotificationType)
            .Select(x => new NotificationDto(
                x.Id,
                x.NotificationTypeId,
                x.NotificationType.Name,
                x.Title,
                x.Body,
                x.IsRead,
                x.DeepLink,
                x.Data,
                x.CreatedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (notification == null)
            throw new NotFoundException($"Notification with id {request.NotificationId} not found");

        return notification;
    }
}
