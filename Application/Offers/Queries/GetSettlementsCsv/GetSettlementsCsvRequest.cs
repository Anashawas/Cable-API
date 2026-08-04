using System.Globalization;
using System.Text;
using Application.Offers.Queries.GetSettlements;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetSettlementsCsv;

/// <summary>
/// C4 — CSV export of settlements honoring the same filters as the list.
/// Returns the CSV content; the route serves it as text/csv.
/// </summary>
public record GetSettlementsCsvRequest(
    int? Status = null, int? Month = null, int? Year = null,
    int? PeriodType = null, int? Week = null, string? Search = null)
    : IRequest<string>;

public class GetSettlementsCsvRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetSettlementsCsvRequest, string>
{
    public async Task<string> Handle(GetSettlementsCsvRequest request, CancellationToken cancellationToken)
    {
        var settlements = await SettlementQueryHelper.ApplyFilters(
                applicationDbContext, request.Status, request.Month, request.Year,
                request.PeriodType, request.Week, request.Search)
            .OrderByDescending(x => x.PeriodYear)
            .ThenByDescending(x => x.PeriodWeek)
            .ToListAsync(cancellationToken);

        var rows = await SettlementQueryHelper.MapAsync(applicationDbContext, settlements, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("SettlementId,ProviderType,ProviderId,ProviderName,OwnerId,OwnerName,PeriodYear,PeriodWeek," +
                      "PartnerTxCount,PartnerTxAmount,PartnerCommission,OfferTxCount,OfferPayment," +
                      "NetBalance,WalletApplied,Outstanding,WalletBalanceNow,Status,PaidAt,CreatedAt");

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                r.Id,
                Csv(r.ProviderDetails.ProviderType),
                r.ProviderDetails.ProviderId,
                Csv(r.ProviderDetails.Name),
                r.OwnerDetails.OwnerId,
                Csv(r.OwnerDetails.Name),
                r.PeriodYear,
                r.PeriodWeek,
                r.PartnerTransactions.TransactionCount,
                r.PartnerTransactions.TransactionAmount.ToString(CultureInfo.InvariantCulture),
                r.PartnerTransactions.CommissionAmount.ToString(CultureInfo.InvariantCulture),
                r.OfferTransactions.TransactionCount,
                r.OfferTransactions.PaymentAmount.ToString(CultureInfo.InvariantCulture),
                r.NetBalance.ToString(CultureInfo.InvariantCulture),
                r.WalletApplied.ToString(CultureInfo.InvariantCulture),
                r.OutstandingAmount.ToString(CultureInfo.InvariantCulture),
                r.CurrentWalletBalance?.ToString(CultureInfo.InvariantCulture) ?? "",
                ((SettlementStatus)r.SettlementStatus).ToString(),
                r.PaidAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                r.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
