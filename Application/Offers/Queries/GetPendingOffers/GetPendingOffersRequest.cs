using Application.Common.Models;
using Application.Offers.Queries.GetActiveOffers;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetPendingOffers;

public record GetPendingOffersRequest(int? Page = null, int? PageSize = null) : IRequest<PagedResult<OfferDto>>;

public class GetPendingOffersRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetPendingOffersRequest, PagedResult<OfferDto>>
{
    public async Task<PagedResult<OfferDto>> Handle(GetPendingOffersRequest request, CancellationToken cancellationToken)
    {
        var paged = await applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Include(x => x.ProposedByUser)
            .Where(x => !x.IsDeleted
                         && x.ApprovalStatus == (int)OfferApprovalStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        return paged.As(paged.Items.Select(x => new OfferDto(
            x.Id, x.Title, x.TitleAr, x.Description, x.DescriptionAr,
            x.ProviderType, x.ProviderId, null,
            x.ProposedByUserId, x.ProposedByUser?.Name,
            x.ApprovalStatus, x.PointsCost, x.MonetaryValue, x.CurrencyCode,
            x.MaxUsesPerUser, x.MaxTotalUses, x.CurrentTotalUses,
            x.OfferCodeExpirySeconds,
            !string.IsNullOrEmpty(x.ImageUrl)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableOfferAttachments, x.ImageUrl)
                : null,
            x.ValidFrom, x.ValidTo,
            x.IsActive, x.CreatedAt, x.PointsPriceValue
        )).ToList());
    }
}
