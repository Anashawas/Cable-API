using Application.Common.Models;
using Application.Common.Interfaces;
using Cable.Core.Enums;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetPendingUpdateRequests;

public record GetPendingUpdateRequestsRequest(RequestStatus? Status, int? Page = null, int? PageSize = null) : IRequest<PagedResult<GetPendingUpdateRequestsDto>>;

public class GetPendingUpdateRequestsRequestHandler(
    IApplicationDbContext context,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetPendingUpdateRequestsRequest, PagedResult<GetPendingUpdateRequestsDto>>
{
    public async Task<PagedResult<GetPendingUpdateRequestsDto>> Handle(GetPendingUpdateRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.ChargingPointUpdateRequests
            .AsNoTracking()
            .Include(x => x.ChargingPoint)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.AttachmentChanges)
                .ThenInclude(a => a.ExistingAttachment)
            .Where(x => !x.IsDeleted);

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

/// <summary>Shared entity → list-DTO mapping (used by pending + my-requests).</summary>
public static class UpdateRequestListMapper
{
    public static GetPendingUpdateRequestsDto ToDto(
        ChargingPointUpdateRequest x,
        UpdateRequestDiffHelper.NameLookups names,
        IUploadFileService uploadFileService)
    {
        var diff = UpdateRequestDiffHelper.Build(x, names, uploadFileService);
        return new GetPendingUpdateRequestsDto(
            x.Id,
            x.ChargingPointId,
            x.ChargingPoint.Name,
            x.RequestedByUserId,
            x.RequestedBy?.Name,
            x.RequestedBy?.Phone,
            x.RequestStatus,
            x.CreatedAt,
            x.ReviewedAt,
            x.ReviewedByUserId,
            x.ReviewedBy?.Name,
            x.RejectionReason,
            diff.Changes,
            diff.Attachments,
            diff.RiskFlags);
    }
}
