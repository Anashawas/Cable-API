using Application.Common.Security;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetLoyaltySummary;

public record DailyIssuedRedeemedDto(DateTime Day, int Issued, int Redeemed);

public record TopEarnerDto(int UserId, string? UserName, int Points);

public record LoyaltySummaryDto(
    // Liability — the money the program owes right now (not affected by date filters)
    int OutstandingLiabilityPoints,
    decimal? EstimatedLiabilityValue,
    string? LiabilityCurrencyCode,
    // Windowed aggregates (filtered by seasonId / from / to when provided)
    int TotalPointsIssued,
    int TotalPointsRedeemed,
    int TotalPointsExpired,
    int TotalPointsAdminAdjusted,
    double RedemptionRatePct,
    int TotalRedemptions,
    // Membership
    int ActiveMembers,
    int BlockedUsers,
    // Series & leaders (windowed; series defaults to the last 30 days)
    List<DailyIssuedRedeemedDto> IssuedVsRedeemedDaily,
    List<TopEarnerDto> TopEarners
);

/// <summary>
/// I1 — program-health dashboard. The headline number is the outstanding points
/// liability (sum of all current balances) and its estimated monetary value via
/// the default conversion rate. Admin role required.
/// </summary>
public record GetLoyaltySummaryRequest(
    int? SeasonId = null,
    DateTime? From = null,
    DateTime? To = null
) : IRequest<LoyaltySummaryDto>;

public class GetLoyaltySummaryRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetLoyaltySummaryRequest, LoyaltySummaryDto>
{
    public async Task<LoyaltySummaryDto> Handle(GetLoyaltySummaryRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        // ---- Liability (always "now", never windowed) ----
        var outstanding = await applicationDbContext.UserLoyaltyAccounts
            .Where(a => !a.IsDeleted)
            .SumAsync(a => (int?)a.CurrentBalance, cancellationToken) ?? 0;

        var defaultRate = await applicationDbContext.PointsConversionRates
            .AsNoTracking()
            .Where(r => r.IsActive && !r.IsDeleted)
            .OrderByDescending(r => r.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);

        decimal? liabilityValue = defaultRate is { PointsPerUnit: > 0 }
            ? Math.Round(outstanding / (decimal)defaultRate.PointsPerUnit, 3)
            : null;

        // ---- Windowed transaction aggregates ----
        var tx = applicationDbContext.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (request.SeasonId.HasValue)
            tx = tx.Where(t => t.LoyaltySeasonId == request.SeasonId.Value);
        if (request.From.HasValue)
            tx = tx.Where(t => t.CreatedAt >= request.From.Value);
        if (request.To.HasValue)
            tx = tx.Where(t => t.CreatedAt <= request.To.Value);

        var byType = await tx
            .GroupBy(t => t.TransactionType)
            .Select(g => new { Type = g.Key, Sum = g.Sum(t => t.Points), Count = g.Count() })
            .ToListAsync(cancellationToken);

        var issued = await tx.Where(t => t.Points > 0)
            .SumAsync(t => (int?)t.Points, cancellationToken) ?? 0;
        var redeemed = Math.Abs(byType.FirstOrDefault(x => x.Type == (int)TransactionType.Redeem)?.Sum ?? 0);
        var expired = Math.Abs(byType.FirstOrDefault(x => x.Type == (int)TransactionType.Expired)?.Sum ?? 0);
        var adminAdjusted = byType.FirstOrDefault(x => x.Type == (int)TransactionType.AdminAdjust)?.Sum ?? 0;

        var redemptionRate = issued > 0 ? Math.Round(redeemed * 100.0 / issued, 2) : 0;

        // ---- Redemption count (windowed) ----
        var redemptionsQuery = applicationDbContext.UserRewardRedemptions
            .AsNoTracking()
            .Where(r => !r.IsDeleted);
        if (request.From.HasValue) redemptionsQuery = redemptionsQuery.Where(r => r.RedeemedAt >= request.From.Value);
        if (request.To.HasValue) redemptionsQuery = redemptionsQuery.Where(r => r.RedeemedAt <= request.To.Value);
        var totalRedemptions = await redemptionsQuery.CountAsync(cancellationToken);

        // ---- Membership ----
        var activeMembers = await applicationDbContext.UserLoyaltyAccounts
            .CountAsync(a => !a.IsDeleted, cancellationToken);
        var now = DateTime.UtcNow;
        var blockedUsers = await applicationDbContext.UserLoyaltyAccounts
            .CountAsync(a => !a.IsDeleted && a.IsBlocked
                             && (a.BlockedUntil == null || a.BlockedUntil >= now), cancellationToken);

        // ---- Daily issued vs redeemed (window, or last 30 days by default) ----
        var seriesFrom = (request.From ?? now.AddDays(-30)).Date;
        var seriesTo = (request.To ?? now).Date.AddDays(1);

        var seriesTx = applicationDbContext.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.CreatedAt >= seriesFrom && t.CreatedAt < seriesTo);
        if (request.SeasonId.HasValue)
            seriesTx = seriesTx.Where(t => t.LoyaltySeasonId == request.SeasonId.Value);

        var dailyRaw = await seriesTx
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new
            {
                Day = g.Key,
                Issued = g.Sum(t => t.Points > 0 ? t.Points : 0),
                Redeemed = g.Sum(t => t.TransactionType == (int)TransactionType.Redeem ? t.Points : 0)
            })
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken);

        var daily = dailyRaw
            .Select(d => new DailyIssuedRedeemedDto(d.Day, d.Issued, Math.Abs(d.Redeemed)))
            .ToList();

        // ---- Top earners (windowed) ----
        var topEarnersRaw = await tx
            .Where(t => t.Points > 0)
            .GroupBy(t => new { t.Account.UserId, t.Account.User.Name })
            .Select(g => new { g.Key.UserId, g.Key.Name, Points = g.Sum(t => t.Points) })
            .OrderByDescending(e => e.Points)
            .Take(10)
            .ToListAsync(cancellationToken);

        var topEarners = topEarnersRaw
            .Select(e => new TopEarnerDto(e.UserId, e.Name, e.Points))
            .ToList();

        return new LoyaltySummaryDto(
            outstanding,
            liabilityValue,
            defaultRate?.CurrencyCode,
            issued,
            redeemed,
            expired,
            adminAdjusted,
            redemptionRate,
            totalRedemptions,
            activeMembers,
            blockedUsers,
            daily,
            topEarners);
    }
}
