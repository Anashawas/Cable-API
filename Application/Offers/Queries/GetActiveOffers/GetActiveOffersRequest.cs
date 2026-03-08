using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetActiveOffers;

public record GetActiveOffersRequest(string? ProviderType = null) : IRequest<List<OfferDto>>;

public class GetActiveOffersRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetActiveOffersRequest, List<OfferDto>>
{
    public async Task<List<OfferDto>> Handle(GetActiveOffersRequest request, CancellationToken cancellationToken)
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

        var offers = await query
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
