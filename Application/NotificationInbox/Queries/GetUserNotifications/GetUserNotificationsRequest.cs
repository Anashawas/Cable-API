using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetUserNotifications;

public record GetUserNotificationsRequest(
    int PageNumber = 1,
    int PageSize = 20,
    bool? IsRead = null
) : IRequest<GetUserNotificationsDto>;

public class GetUserNotificationsQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserNotificationsRequest, GetUserNotificationsDto>
{
    public async Task<GetUserNotificationsDto> Handle(
        GetUserNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");
        
        var query = applicationDbContext.NotificationInboxes
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && !x.IsDeleted);
        
        if (request.IsRead.HasValue)
        {
            query = query.Where(x => x.IsRead == request.IsRead.Value);
        }
        
        var totalCount = await query.CountAsync(cancellationToken);
        
        var notifications = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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
            .ToListAsync(cancellationToken);

        return new GetUserNotificationsDto(
            notifications,
            totalCount,
            request.PageNumber,
            request.PageSize
        );
    }
}
