using Application.Offers.Queries.GetActiveOffers;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetPendingOffers;

public record GetPendingOffersRequest() : IRequest<List<OfferDto>>;

public class GetPendingOffersRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetPendingOffersRequest, List<OfferDto>>
{
    public async Task<List<OfferDto>> Handle(GetPendingOffersRequest request, CancellationToken cancellationToken)
    {
        var offers = await applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Include(x => x.ProposedByUser)
            .Where(x => !x.IsDeleted
                         && x.ApprovalStatus == (int)OfferApprovalStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return offers.Select(x => new OfferDto(
            x.Id, x.Title, x.TitleAr, x.Description, x.DescriptionAr,
            x.ProviderType, x.ProviderId, null,
            x.ProposedByUserId, x.ProposedByUser?.Name,
            x.ApprovalStatus, x.PointsCost, x.MonetaryValue, x.CurrencyCode,
            x.MaxUsesPerUser, x.MaxTotalUses, x.CurrentTotalUses,
            x.OfferCodeExpiryMinutes, x.ImageUrl, x.ValidFrom, x.ValidTo,
            x.IsActive, x.CreatedAt
        )).ToList();
    }
}
