using System.Globalization;
using Application.Common.Security;
using Application.Loyalty.Queries.GetProviderActivity;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetSettlementTransactions;

public record SettlementTransactionsDto(
    int SettlementId,
    string ProviderType,
    int ProviderId,
    int PeriodYear,
    int PeriodWeek,
    DateTime FromUtc,
    DateTime ToUtc,
    List<ProviderActivityDto> Items
);

/// <summary>
/// C1 — the transaction line-items behind one settlement: every COMPLETED offer
/// redemption and partner charge for the settlement's provider whose completion
/// falls inside the settlement's week (Sunday–Saturday, same calendar rule the
/// settlement engine uses). Lets admins verify totals and resolve disputes.
/// Admin role required.
/// </summary>
public record GetSettlementTransactionsRequest(int SettlementId) : IRequest<SettlementTransactionsDto>;

public class GetSettlementTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetSettlementTransactionsRequest, SettlementTransactionsDto>
{
    public async Task<SettlementTransactionsDto> Handle(GetSettlementTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var settlement = await applicationDbContext.ProviderSettlements
                             .AsNoTracking()
                             .FirstOrDefaultAsync(x => x.Id == request.SettlementId && !x.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"can not find settlement with id {request.SettlementId}");

        if (settlement.PeriodType != (int)SettlementPeriodType.Weekly)
            throw new DataValidationException("SettlementId",
                "Line items are only supported for weekly settlements");

        var (from, toExclusive) = GetWeekWindow(settlement.PeriodYear, settlement.PeriodWeek);

        var items = new List<ProviderActivityDto>();

        var offers = await applicationDbContext.OfferTransactions
            .AsNoTracking()
            .Where(t => t.ProviderType == settlement.ProviderType
                        && t.ProviderId == settlement.ProviderId
                        && !t.IsDeleted
                        && t.Status == (int)OfferTransactionStatus.Completed
                        && t.CompletedAt != null
                        && t.CompletedAt >= from && t.CompletedAt < toExclusive)
            .Select(t => new
            {
                t.Id, t.UserId, UserName = t.User != null ? t.User.Name : null,
                t.OfferCode, t.Status, t.PointsDeducted, t.MonetaryValue,
                t.CurrencyCode, t.CreatedAt, t.CompletedAt
            })
            .ToListAsync(cancellationToken);

        items.AddRange(offers.Select(t => new ProviderActivityDto(
            "Offer", t.Id, t.UserId, t.UserName, t.OfferCode,
            t.Status, ((OfferTransactionStatus)t.Status).ToString(),
            -t.PointsDeducted, t.MonetaryValue, t.CurrencyCode,
            t.CreatedAt, t.CompletedAt)));

        var partners = await applicationDbContext.PartnerTransactions
            .AsNoTracking()
            .Where(t => t.ProviderType == settlement.ProviderType
                        && t.ProviderId == settlement.ProviderId
                        && !t.IsDeleted
                        && t.Status == (int)PartnerTransactionStatus.Completed
                        && t.CompletedAt != null
                        && t.CompletedAt >= from && t.CompletedAt < toExclusive)
            .Select(t => new
            {
                t.Id, t.UserId, UserName = t.User != null ? t.User.Name : null,
                t.TransactionCode, t.Status, t.PointsAwarded, t.TransactionAmount,
                t.CommissionAmount, t.CurrencyCode, t.CreatedAt, t.CompletedAt
            })
            .ToListAsync(cancellationToken);

        items.AddRange(partners.Select(t => new ProviderActivityDto(
            "Partner", t.Id, t.UserId, t.UserName, t.TransactionCode,
            t.Status, ((PartnerTransactionStatus)t.Status).ToString(),
            t.PointsAwarded ?? 0, t.TransactionAmount, t.CurrencyCode,
            t.CreatedAt, t.CompletedAt)));

        return new SettlementTransactionsDto(
            settlement.Id,
            settlement.ProviderType,
            settlement.ProviderId,
            settlement.PeriodYear,
            settlement.PeriodWeek,
            from,
            toExclusive.AddDays(-1), // inclusive Saturday for display
            items.OrderByDescending(i => i.CompletedAt ?? i.CreatedAt).ToList());
    }

    /// <summary>
    /// Inverse of the settlement engine's week rule
    /// (CalendarWeekRule.FirstDay, DayOfWeek.Sunday): week 1 starts Jan 1 and runs
    /// to the first Sunday; every later week starts on a Sunday.
    /// Returns [start, endExclusive).
    /// </summary>
    internal static (DateTime From, DateTime ToExclusive) GetWeekWindow(int year, int week)
    {
        var jan1 = new DateTime(year, 1, 1);
        var offsetToSunday = ((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7;
        var firstNewWeekSunday = jan1.AddDays(offsetToSunday == 0 ? 7 : offsetToSunday);

        DateTime start, end;
        if (week <= 1)
        {
            start = jan1;
            end = firstNewWeekSunday;
        }
        else
        {
            start = firstNewWeekSunday.AddDays((week - 2) * 7);
            end = start.AddDays(7);
            // Clamp into the settlement's year (last week may cross into Jan 1 next year).
            if (end > new DateTime(year + 1, 1, 1))
                end = new DateTime(year + 1, 1, 1);
        }

        // Sanity: the engine's own rule must agree with our window boundaries.
        var calendar = CultureInfo.InvariantCulture.Calendar;
        if (calendar.GetWeekOfYear(start, CalendarWeekRule.FirstDay, DayOfWeek.Sunday) != week)
            throw new DataValidationException("SettlementId", $"Invalid week {week} for year {year}");

        return (start, end);
    }
}
