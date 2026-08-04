using Application.Common.Models;
using Application.Common.Security;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetFlaggedActivity;

public record FlaggedActivityDto(
    string RuleCode,
    string RuleName,
    int UserId,
    string? UserName,
    int Metric,
    int WindowHours
);

/// <summary>
/// L1 — heuristic fraud/abuse review queue (v1). Three velocity rules over the
/// ledger; thresholds are conservative defaults and can be tuned via the query.
/// Admin role required.
/// </summary>
public record GetFlaggedActivityRequest(
    int WindowHours = 24,
    int EarnVelocityThreshold = 10,
    int RedemptionVelocityThreshold = 3,
    int BalanceSwingThreshold = 1000,
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<FlaggedActivityDto>>;

public class GetFlaggedActivityRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetFlaggedActivityRequest, PagedResult<FlaggedActivityDto>>
{
    public async Task<PagedResult<FlaggedActivityDto>> Handle(GetFlaggedActivityRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var windowHours = Math.Clamp(request.WindowHours, 1, 24 * 30);
        var since = DateTime.UtcNow.AddHours(-windowHours);
        var flagged = new List<FlaggedActivityDto>();

        // Rule 1 — earn velocity: many earn transactions in the window.
        var earnVelocity = await applicationDbContext.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted
                        && t.TransactionType == (int)TransactionType.Earn
                        && t.CreatedAt >= since)
            .GroupBy(t => new { t.Account.UserId, t.Account.User.Name })
            .Select(g => new { g.Key.UserId, g.Key.Name, Count = g.Count() })
            .Where(x => x.Count >= request.EarnVelocityThreshold)
            .ToListAsync(cancellationToken);

        flagged.AddRange(earnVelocity.Select(x => new FlaggedActivityDto(
            "EARN_VELOCITY", "Unusually many earn transactions",
            x.UserId, x.Name, x.Count, windowHours)));

        // Rule 2 — redemption velocity: many reward redemptions in the window.
        var redemptionVelocity = await applicationDbContext.UserRewardRedemptions
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.RedeemedAt >= since)
            .GroupBy(r => new { r.UserId, r.User.Name })
            .Select(g => new { g.Key.UserId, g.Key.Name, Count = g.Count() })
            .Where(x => x.Count >= request.RedemptionVelocityThreshold)
            .ToListAsync(cancellationToken);

        flagged.AddRange(redemptionVelocity.Select(x => new FlaggedActivityDto(
            "REDEMPTION_VELOCITY", "Unusually many reward redemptions",
            x.UserId, x.Name, x.Count, windowHours)));

        // Rule 3 — large balance swing from non-admin activity in the window.
        var swings = await applicationDbContext.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted
                        && t.TransactionType != (int)TransactionType.AdminAdjust
                        && t.CreatedAt >= since)
            .GroupBy(t => new { t.Account.UserId, t.Account.User.Name })
            .Select(g => new { g.Key.UserId, g.Key.Name, Net = g.Sum(t => t.Points) })
            .ToListAsync(cancellationToken);

        flagged.AddRange(swings
            .Where(x => Math.Abs(x.Net) >= request.BalanceSwingThreshold)
            .Select(x => new FlaggedActivityDto(
                "BALANCE_SWING", "Large balance change from non-admin activity",
                x.UserId, x.Name, x.Net, windowHours)));

        return flagged
            .OrderByDescending(f => Math.Abs(f.Metric))
            .ToList()
            .ToOptionallyPaginated(request.Page, request.PageSize);
    }
}
