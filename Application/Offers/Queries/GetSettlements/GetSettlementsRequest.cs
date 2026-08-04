using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetSettlements;

/// <summary>
/// Legacy settlements list. The extra optional fields are dispatch hints for the
/// route: when Search/Page/PageSize is present the route sends
/// GetSettlementsPagedRequest instead (C6) — this handler ignores them.
/// </summary>
public record GetSettlementsRequest(
    int? Status = null, int? Month = null, int? Year = null,
    int? PeriodType = null, int? Week = null,
    string? Search = null, int? Page = null, int? PageSize = null)
    : IRequest<List<ProviderSettlementDto>>;

public class GetSettlementsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetSettlementsRequest, List<ProviderSettlementDto>>
{
    public async Task<List<ProviderSettlementDto>> Handle(GetSettlementsRequest request,
        CancellationToken cancellationToken)
    {
        var query = SettlementQueryHelper.ApplyFilters(
            applicationDbContext, request.Status, request.Month, request.Year,
            request.PeriodType, request.Week, null);

        var settlements = await query
            .OrderByDescending(x => x.PeriodYear)
            .ThenByDescending(x => x.PeriodMonth)
            .ThenByDescending(x => x.PeriodWeek)
            .ToListAsync(cancellationToken);

        return await SettlementQueryHelper.MapAsync(applicationDbContext, settlements, cancellationToken);
    }
}

/// <summary>
/// Shared filtering + enrichment for the settlement list endpoints (legacy,
/// paged and CSV). Enrichment batches provider details AND the provider's
/// current wallet balance (removes the admin's per-row N+1 balance calls).
/// </summary>
public static class SettlementQueryHelper
{
    public static IQueryable<ProviderSettlement> ApplyFilters(
        IApplicationDbContext db,
        int? status, int? month, int? year, int? periodType, int? week, string? search)
    {
        var query = db.ProviderSettlements
            .AsNoTracking()
            .Include(x => x.ProviderOwner)
            .Where(x => !x.IsDeleted);

        if (status.HasValue)
            query = query.Where(x => x.SettlementStatus == status.Value);

        if (year.HasValue)
            query = query.Where(x => x.PeriodYear == year.Value);

        if (month.HasValue)
            query = query.Where(x => x.PeriodMonth == month.Value);

        if (periodType.HasValue)
            query = query.Where(x => x.PeriodType == periodType.Value);

        if (week.HasValue)
            query = query.Where(x => x.PeriodWeek == week.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var searchId = int.TryParse(s, out var id) ? id : (int?)null;
            query = query.Where(x =>
                (x.ProviderOwner != null && x.ProviderOwner.Name != null && x.ProviderOwner.Name.Contains(s))
                || (searchId != null && x.ProviderId == searchId.Value));
        }

        return query;
    }

    public static async Task<List<ProviderSettlementDto>> MapAsync(
        IApplicationDbContext db,
        List<ProviderSettlement> settlements,
        CancellationToken cancellationToken)
    {
        var cpIds = settlements.Where(x => x.ProviderType == "ChargingPoint").Select(x => x.ProviderId).Distinct().ToList();
        var spIds = settlements.Where(x => x.ProviderType == "ServiceProvider").Select(x => x.ProviderId).Distinct().ToList();

        var chargingPoints = cpIds.Count > 0
            ? await db.ChargingPoints.AsNoTracking()
                .Where(x => cpIds.Contains(x.Id) && !x.IsDeleted)
                .ToDictionaryAsync(x => x.Id, cancellationToken)
            : new Dictionary<int, ChargingPoint>();

        var serviceProviders = spIds.Count > 0
            ? await db.ServiceProviders.AsNoTracking()
                .Where(x => spIds.Contains(x.Id) && !x.IsDeleted)
                .ToDictionaryAsync(x => x.Id, cancellationToken)
            : new Dictionary<int, ServiceProvider>();

        return settlements.Select(x =>
        {
            string? providerName = null, providerPhone = null, providerAddress = null, providerIcon = null;
            decimal? walletBalance = null;

            if (x.ProviderType == "ChargingPoint" && chargingPoints.TryGetValue(x.ProviderId, out var cp))
            {
                providerName = cp.Name;
                providerPhone = cp.Phone;
                providerAddress = cp.Address;
                providerIcon = cp.Icon;
                walletBalance = cp.WalletBalance;
            }
            else if (x.ProviderType == "ServiceProvider" && serviceProviders.TryGetValue(x.ProviderId, out var sp))
            {
                providerName = sp.Name;
                providerPhone = sp.Phone;
                providerAddress = sp.Address;
                providerIcon = sp.Icon;
                walletBalance = sp.WalletBalance;
            }

            return new ProviderSettlementDto(
                x.Id,
                new ProviderDetailsDto(x.ProviderType, x.ProviderId, providerName, providerPhone, providerAddress, providerIcon),
                new OwnerDetailsDto(x.ProviderOwnerId, x.ProviderOwner?.Name, x.ProviderOwner?.Email, x.ProviderOwner?.Phone),
                x.PeriodYear, x.PeriodMonth,
                x.PeriodType, x.PeriodWeek,
                new PartnerTransactionSummaryDto(x.PartnerTransactionCount, x.PartnerTransactionAmount, x.PartnerCommissionAmount, x.TotalPointsAwarded),
                new OfferTransactionSummaryDto(x.OfferTransactionCount, x.OfferPaymentAmount, x.TotalPointsDeducted),
                x.NetBalance,
                x.WalletApplied,
                x.PartnerCommissionAmount - x.WalletApplied, // OutstandingAmount
                x.SettlementStatus, x.PaidAt,
                x.AdminNote, x.CreatedAt,
                walletBalance
            );
        }).ToList();
    }
}
