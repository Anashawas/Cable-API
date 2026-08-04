using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetNotificationBatchById;

/// <summary>
/// Admin query: returns full statistics + recipient breakdown for a single notification send
/// identified by its BatchId.
/// </summary>
public record GetNotificationBatchByIdRequest(Guid BatchId)
    : IRequest<NotificationBatchDetailDto>;

public class GetNotificationBatchByIdQueryHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetNotificationBatchByIdRequest, NotificationBatchDetailDto>
{
    public async Task<NotificationBatchDetailDto> Handle(
        GetNotificationBatchByIdRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await applicationDbContext.NotificationInboxes
            .AsNoTracking()
            .Where(x => x.BatchId == request.BatchId && !x.IsDeleted)
            .Include(x => x.NotificationType)
            .Include(x => x.User)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            throw new NotFoundException($"can not find notification batch with id {request.BatchId}");
        }

        var first = rows[0];
        var total = rows.Count;
        var read  = rows.Count(r => r.IsRead);
        var unread = total - read;
        var readRate = total == 0 ? 0m : Math.Round((decimal)read * 100m / total, 2);

        var recipients = rows
            .OrderByDescending(r => r.IsRead)
            .ThenBy(r => r.UserId)
            .Select(r => new NotificationBatchRecipientDto(
                r.UserId,
                r.User?.Name,
                r.User?.Phone,
                r.User?.Email,
                r.IsRead,
                r.CreatedAt))
            .ToList();

        return new NotificationBatchDetailDto(
            request.BatchId,
            first.NotificationTypeId,
            first.NotificationType?.Name ?? string.Empty,
            first.Title,
            first.Body,
            first.DeepLink,
            first.Data,
            first.CreatedAt,
            total,
            read,
            unread,
            readRate,
            recipients);
    }
}
