using Application.Common.Models;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetActiveOffers;

public record GetActiveOffersRequest(string? ProviderType = null, int? Page = null, int? PageSize = null) : IRequest<PagedResult<OfferDto>>;

public class GetActiveOffersRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetActiveOffersRequest, PagedResult<OfferDto>>
{
    public async Task<PagedResult<OfferDto>> Handle(GetActiveOffersRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var query = applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Include(x => x.ProposedByUser)
            .Where(x => !x.IsDeleted
                         && x.IsActive
                         && x.ApprovalStatus == (int)OfferApprovalStatus.Approved
                         && x.ValidFrom <= now
                         && (x.ValidTo == null || x.ValidTo >= now));

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(x => x.ProviderType == request.ProviderType);

        var paged = await query
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
