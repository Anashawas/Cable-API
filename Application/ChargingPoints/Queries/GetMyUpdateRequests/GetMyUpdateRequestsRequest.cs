using Application.Common.Models;
using Application.ChargingPoints.Queries.GetPendingUpdateRequests;
using Application.Common.Interfaces;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetMyUpdateRequests;

public record GetMyUpdateRequestsRequest(RequestStatus? Status, int? Page = null, int? PageSize = null) : IRequest<PagedResult<GetPendingUpdateRequestsDto>>;

public class GetMyUpdateRequestsRequestHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetMyUpdateRequestsRequest, PagedResult<GetPendingUpdateRequestsDto>>
{
    public async Task<PagedResult<GetPendingUpdateRequestsDto>> Handle(GetMyUpdateRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.ChargingPointUpdateRequests
            .AsNoTracking()
            .Include(x => x.ChargingPoint)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.AttachmentChanges)
                .ThenInclude(a => a.ExistingAttachment)
            .Where(x => !x.IsDeleted && x.RequestedByUserId == currentUserService.UserId);

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.RequestStatus == request.Status.Value);
        }

        var page = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        var names = await UpdateRequestDiffHelper.LoadNamesAsync(context, page.Items, cancellationToken);
        return page.As(page.Items
            .Select(x => UpdateRequestListMapper.ToDto(x, names, uploadFileService))
            .ToList());
    }
}
