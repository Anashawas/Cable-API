using Application.ChargingPoints.Queries.GetPendingUpdateRequests;
using Application.Common.Interfaces;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetMyUpdateRequests;

public record GetMyUpdateRequestsRequest(RequestStatus? Status) : IRequest<List<GetPendingUpdateRequestsDto>>;

public class GetMyUpdateRequestsRequestHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyUpdateRequestsRequest, List<GetPendingUpdateRequestsDto>>
{
    public async Task<List<GetPendingUpdateRequestsDto>> Handle(GetMyUpdateRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Where(x => !x.IsDeleted && x.RequestedByUserId == currentUserService.UserId);

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.RequestStatus == request.Status.Value);
        }

        var results = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new GetPendingUpdateRequestsDto(
                x.Id,
                x.ChargingPointId,
                x.ChargingPoint.Name,
                x.RequestedByUserId,
                x.RequestedBy.Name,
                x.RequestedBy.Phone,
                x.RequestStatus,
                x.CreatedAt,
                x.ReviewedAt,
                x.ReviewedByUserId,
                x.ReviewedBy != null ? x.ReviewedBy.Name : null,
                x.RejectionReason
            ))
            .ToListAsync(cancellationToken);

        return results;
    }
}
