using Application.Common.Models;
using Application.Common.Security;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetRewardPerformance;

public record RewardPerformanceDto(
    int RewardId,
    string RewardName,
    bool IsActive,
    int PointsCost,
    int Redemptions,
    int CancelledRedemptions,
    int PointsSpent,
    int? RemainingStock
);

/// <summary>
/// I2 — per-reward performance: redemption counts and points spent within the
/// window, plus remaining stock (null = unlimited). Admin role required.
/// </summary>
public record GetRewardPerformanceRequest(
    DateTime? From = null,
    DateTime? To = null,
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<RewardPerformanceDto>>;

public class GetRewardPerformanceRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetRewardPerformanceRequest, PagedResult<RewardPerformanceDto>>
{
    public async Task<PagedResult<RewardPerformanceDto>> Handle(GetRewardPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var redemptions = applicationDbContext.UserRewardRedemptions
            .AsNoTracking()
            .Where(r => !r.IsDeleted);

        if (request.From.HasValue)
            redemptions = redemptions.Where(r => r.RedeemedAt >= request.From.Value);
        if (request.To.HasValue)
            redemptions = redemptions.Where(r => r.RedeemedAt <= request.To.Value);

        var stats = await redemptions
            .GroupBy(r => r.LoyaltyRewardId)
            .Select(g => new
            {
                RewardId = g.Key,
                Redemptions = g.Count(r => r.Status != (int)RedemptionStatus.Cancelled),
                Cancelled = g.Count(r => r.Status == (int)RedemptionStatus.Cancelled),
                PointsSpent = g.Where(r => r.Status != (int)RedemptionStatus.Cancelled)
                    .Sum(r => (int?)r.PointsSpent) ?? 0
            })
            .ToDictionaryAsync(x => x.RewardId, cancellationToken);

        var rewards = await applicationDbContext.LoyaltyRewards
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Select(r => new
            {
                r.Id, r.Name, r.IsActive, r.PointsCost,
                r.MaxRedemptions, r.CurrentRedemptions
            })
            .ToListAsync(cancellationToken);

        return rewards
            .Select(r =>
            {
                stats.TryGetValue(r.Id, out var s);
                return new RewardPerformanceDto(
                    r.Id,
                    r.Name,
                    r.IsActive,
                    r.PointsCost,
                    s?.Redemptions ?? 0,
                    s?.Cancelled ?? 0,
                    s?.PointsSpent ?? 0,
                    r.MaxRedemptions.HasValue
                        ? Math.Max(0, r.MaxRedemptions.Value - r.CurrentRedemptions)
                        : null);
            })
            .OrderByDescending(r => r.Redemptions)
            .ToList()
            .ToOptionallyPaginated(request.Page, request.PageSize);
    }
}
