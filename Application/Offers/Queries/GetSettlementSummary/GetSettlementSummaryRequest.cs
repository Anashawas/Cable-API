using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetSettlementSummary;

public record SettlementSummaryDto(
    int TotalSettlements,
    int TotalPartnerTransactions,
    decimal TotalPartnerTransactionAmount,
    decimal TotalPartnerCommissionAmount,
    int TotalPointsAwarded,
    int TotalOfferTransactions,
    decimal TotalOfferPaymentAmount,
    int TotalPointsDeducted,
    decimal TotalNetAmountDueToProviders,
    int PendingCount,
    int InvoicedCount,
    int PaidCount,
    int DisputedCount
);

public record GetSettlementSummaryRequest(int? Month = null, int? Year = null) : IRequest<SettlementSummaryDto>;

public class GetSettlementSummaryRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetSettlementSummaryRequest, SettlementSummaryDto>
{
    public async Task<SettlementSummaryDto> Handle(GetSettlementSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var query = applicationDbContext.ProviderSettlements
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (request.Year.HasValue)
            query = query.Where(x => x.PeriodYear == request.Year.Value);

        if (request.Month.HasValue)
            query = query.Where(x => x.PeriodMonth == request.Month.Value);

        var settlements = await query.ToListAsync(cancellationToken);

        return new SettlementSummaryDto(
            TotalSettlements: settlements.Count,
            TotalPartnerTransactions: settlements.Sum(x => x.PartnerTransactionCount),
            TotalPartnerTransactionAmount: settlements.Sum(x => x.PartnerTransactionAmount),
            TotalPartnerCommissionAmount: settlements.Sum(x => x.PartnerCommissionAmount),
            TotalPointsAwarded: settlements.Sum(x => x.TotalPointsAwarded),
            TotalOfferTransactions: settlements.Sum(x => x.OfferTransactionCount),
            TotalOfferPaymentAmount: settlements.Sum(x => x.OfferPaymentAmount),
            TotalPointsDeducted: settlements.Sum(x => x.TotalPointsDeducted),
            TotalNetAmountDueToProviders: settlements.Sum(x => x.NetAmountDueToProvider),
            PendingCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Pending),
            InvoicedCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Invoiced),
            PaidCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Paid),
            DisputedCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Disputed)
        );
    }
}
