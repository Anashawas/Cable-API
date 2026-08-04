using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetPendingViewImages;

public record PendingViewImageDto(
    int ChargingPointId,
    string StationName,
    string? CityName,
    int? OwnerId,
    string? OwnerName,
    string ViewImage,
    DateTime? UploadedAt);

/// <summary>Admin: the review queue — stations whose promo image awaits approval.</summary>
public record GetPendingViewImagesRequest(int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<PendingViewImageDto>>;

public class GetPendingViewImagesRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetPendingViewImagesRequest, PagedResult<PendingViewImageDto>>
{
    public async Task<PagedResult<PendingViewImageDto>> Handle(GetPendingViewImagesRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var page = await applicationDbContext.ChargingPoints.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ViewImageStatus == "pending" && x.ViewImage != null)
            .OrderByDescending(x => x.ModifiedAt)
            .Select(x => new
            {
                x.Id, x.Name, x.CityName, x.OwnerId,
                OwnerName = x.Owner != null ? x.Owner.Name : null,
                x.ViewImage, x.ModifiedAt
            })
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        return page.As(page.Items.Select(x => new PendingViewImageDto(
            x.Id,
            x.Name,
            x.CityName,
            x.OwnerId,
            x.OwnerName,
            uploadFileService.GetFilePath(UploadFileFolders.CableViewImages, x.ViewImage!),
            x.ModifiedAt)).ToList());
    }
}
