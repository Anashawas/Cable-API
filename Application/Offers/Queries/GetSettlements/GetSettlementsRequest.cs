using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetSettlements;

public record GetSettlementsRequest(int? Status = null, int? Month = null, int? Year = null)
    : IRequest<List<ProviderSettlementDto>>;

public class GetSettlementsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetSettlementsRequest, List<ProviderSettlementDto>>
{
    public async Task<List<ProviderSettlementDto>> Handle(GetSettlementsRequest request,
        CancellationToken cancellationToken)
    {
        var query = applicationDbContext.ProviderSettlements
            .AsNoTracking()
            .Include(x => x.ProviderOwner)
            .Where(x => !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.SettlementStatus == request.Status.Value);

        if (request.Year.HasValue)
            query = query.Where(x => x.PeriodYear == request.Year.Value);

        if (request.Month.HasValue)
            query = query.Where(x => x.PeriodMonth == request.Month.Value);

        var settlements = await query
            .OrderByDescending(x => x.PeriodYear)
            .ThenByDescending(x => x.PeriodMonth)
            .ToListAsync(cancellationToken);

        return settlements.Select(x => new ProviderSettlementDto(
            x.Id, x.ProviderType, x.ProviderId,
            x.ProviderOwnerId, x.ProviderOwner?.Name,
            x.PeriodYear, x.PeriodMonth,
            x.PartnerTransactionCount, x.PartnerTransactionAmount,
            x.PartnerCommissionAmount, x.TotalPointsAwarded,
            x.OfferTransactionCount, x.OfferPaymentAmount,
            x.TotalPointsDeducted, x.NetAmountDueToProvider,
            x.SettlementStatus, x.InvoicedAt, x.PaidAt,
            x.PaidAmount, x.AdminNote, x.CreatedAt
        )).ToList();
    }
}
