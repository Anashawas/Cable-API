using Application.Common.Models;
using Application.Offers.Queries.GetActiveOffers;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetOffersForProvider;

public record GetOffersForProviderRequest(string ProviderType, int ProviderId, int? Page = null, int? PageSize = null) : IRequest<PagedResult<OfferDto>>;

public class GetOffersForProviderRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetOffersForProviderRequest, PagedResult<OfferDto>>
{
    public async Task<PagedResult<OfferDto>> Handle(GetOffersForProviderRequest request, CancellationToken cancellationToken)
    {
        var paged = await applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Include(x => x.ProposedByUser)
            .Where(x => !x.IsDeleted
                         && x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId)
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
