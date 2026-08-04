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
    decimal TotalNetBalance,
    decimal TotalWalletApplied,
    int PendingCount,
    int PaidCount,
    int DisputedCount,
    decimal TotalOutstandingAmount = 0,   // Σ (commission - walletApplied) — what providers still owe
    decimal TotalDisputedAmount = 0       // Σ |netBalance| of Disputed settlements
);

public record GetSettlementSummaryRequest(
    int? Month = null, int? Year = null,
    int? PeriodType = null, int? Week = null) : IRequest<SettlementSummaryDto>;

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

        if (request.PeriodType.HasValue)
            query = query.Where(x => x.PeriodType == request.PeriodType.Value);

        if (request.Week.HasValue)
            query = query.Where(x => x.PeriodWeek == request.Week.Value);

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
            TotalNetBalance: settlements.Sum(x => x.NetBalance),
            TotalWalletApplied: settlements.Sum(x => x.WalletApplied),
            PendingCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Pending),
            PaidCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Paid),
            DisputedCount: settlements.Count(x => x.SettlementStatus == (int)SettlementStatus.Disputed),
            TotalOutstandingAmount: settlements.Sum(x => x.PartnerCommissionAmount - x.WalletApplied),
            TotalDisputedAmount: settlements
                .Where(x => x.SettlementStatus == (int)SettlementStatus.Disputed)
                .Sum(x => Math.Abs(x.NetBalance))
        );
    }
}
