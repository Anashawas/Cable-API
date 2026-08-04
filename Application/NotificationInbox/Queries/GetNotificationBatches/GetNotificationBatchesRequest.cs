using Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetNotificationBatches;

/// <summary>
/// Admin query: returns a paginated list of notification sends (grouped by BatchId)
/// with delivery + read statistics. One row per send.
/// </summary>
public record GetNotificationBatchesRequest(
    int PageNumber = 1,
    int PageSize = 20,
    int? NotificationTypeId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null
) : IRequest<GetNotificationBatchesDto>;

public class GetNotificationBatchesQueryHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetNotificationBatchesRequest, GetNotificationBatchesDto>
{
    public async Task<GetNotificationBatchesDto> Handle(
        GetNotificationBatchesRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize   = request.PageSize   < 1 ? 20 : (request.PageSize > 100 ? 100 : request.PageSize);

        var baseQuery = applicationDbContext.NotificationInboxes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BatchId != null);

        if (request.NotificationTypeId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.NotificationTypeId == request.NotificationTypeId.Value);
        }

        if (request.FromDate.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.CreatedAt <= request.ToDate.Value);
        }

        // Group by BatchId — one logical row per send.
        var grouped = baseQuery
            .GroupBy(x => x.BatchId!.Value)
            .Select(g => new
            {
                BatchId            = g.Key,
                NotificationTypeId = g.Max(x => x.NotificationTypeId),
                Title              = g.Max(x => x.Title),
                Body               = g.Max(x => x.Body),
                DeepLink           = g.Max(x => x.DeepLink),
                SentAt             = g.Max(x => x.CreatedAt),
                TotalRecipients    = g.Count(),
                ReadCount          = g.Sum(x => x.IsRead ? 1 : 0)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        var pageItems = await grouped
            .OrderByDescending(x => x.SentAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Resolve NotificationType names in a single trip.
        var typeIds = pageItems.Select(x => x.NotificationTypeId).Distinct().ToList();
        var typeNameMap = await applicationDbContext.NotificationTypes
            .AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var batches = pageItems
            .Select(x =>
            {
                var unread   = x.TotalRecipients - x.ReadCount;
                var readRate = x.TotalRecipients == 0
                    ? 0m
                    : Math.Round((decimal)x.ReadCount * 100m / x.TotalRecipients, 2);

                return new NotificationBatchDto(
                    x.BatchId,
                    x.NotificationTypeId,
                    typeNameMap.TryGetValue(x.NotificationTypeId, out var name) ? name : string.Empty,
                    x.Title ?? string.Empty,
                    x.Body ?? string.Empty,
                    x.DeepLink,
                    x.SentAt,
                    x.TotalRecipients,
                    x.ReadCount,
                    unread,
                    readRate);
            })
            .ToList();

        return new GetNotificationBatchesDto(batches, totalCount, pageNumber, pageSize);
    }
}
