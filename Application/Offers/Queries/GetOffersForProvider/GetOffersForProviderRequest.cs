using Application.Offers.Queries.GetActiveOffers;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetOffersForProvider;

public record GetOffersForProviderRequest(string ProviderType, int ProviderId) : IRequest<List<OfferDto>>;

public class GetOffersForProviderRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetOffersForProviderRequest, List<OfferDto>>
{
    public async Task<List<OfferDto>> Handle(GetOffersForProviderRequest request, CancellationToken cancellationToken)
    {
        var offers = await applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Include(x => x.ProposedByUser)
            .Where(x => !x.IsDeleted
                         && x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId)
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
