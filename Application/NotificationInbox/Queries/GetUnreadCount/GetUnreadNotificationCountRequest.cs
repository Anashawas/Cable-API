using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetUnreadCount;

public record GetUnreadNotificationCountRequest : IRequest<UnreadCountDto>;

public class GetUnreadNotificationCountQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUnreadNotificationCountRequest, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(
        GetUnreadNotificationCountRequest request,
        CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");

        var count = await applicationDbContext.NotificationInboxes
            .Where(x => x.UserId == user.Id && !x.IsRead && !x.IsDeleted)
            .CountAsync(cancellationToken);

        return new UnreadCountDto(count);
    }
}

public record UnreadCountDto(int UnreadCount);
