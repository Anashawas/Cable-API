using Application.Common.Interfaces;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetUserActivityStats;

/// <param name="TotalUsers">Every non-deleted account.</param>
/// <param name="DailyActiveUsers">Used the app in the last 24 hours.</param>
/// <param name="WeeklyActiveUsers">Last 7 days.</param>
/// <param name="MonthlyActiveUsers">Last 30 days.</param>
/// <param name="NeverSeen">
/// Registered but never made an authenticated request since tracking began.
/// Expect this to start at nearly every user and fall as people return — it is
/// only meaningful once the app has been live for a while.
/// </param>
/// <param name="NewUsersToday">Registered in the last 24 hours.</param>
/// <param name="NewUsersThisWeek">Registered in the last 7 days.</param>
/// <param name="NewUsersThisMonth">Registered in the last 30 days.</param>
/// <param name="LoggedInLast30Days">
/// Distinct accounts that actually re-authenticated. Much lower than
/// <paramref name="MonthlyActiveUsers"/> by design — access tokens are
/// long-lived, so active users rarely sign in again.
/// </param>
public record UserActivityStatsDto(
    int TotalUsers,
    int DailyActiveUsers,
    int WeeklyActiveUsers,
    int MonthlyActiveUsers,
    int NeverSeen,
    int NewUsersToday,
    int NewUsersThisWeek,
    int NewUsersThisMonth,
    int LoggedInLast30Days,
    DateTime GeneratedAtUtc);

public record GetUserActivityStatsRequest : IRequest<UserActivityStatsDto>;

public class GetUserActivityStatsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserActivityStatsRequest, UserActivityStatsDto>
{
    public async Task<UserActivityStatsDto> Handle(
        GetUserActivityStatsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var now = DateTime.UtcNow;
        var day = now.AddDays(-1);
        var week = now.AddDays(-7);
        var month = now.AddDays(-30);

        // One pass over the table rather than nine COUNT queries.
        var stats = await applicationDbContext.UserAccounts
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Dau = g.Count(u => u.LastSeenAt != null && u.LastSeenAt >= day),
                Wau = g.Count(u => u.LastSeenAt != null && u.LastSeenAt >= week),
                Mau = g.Count(u => u.LastSeenAt != null && u.LastSeenAt >= month),
                NeverSeen = g.Count(u => u.LastSeenAt == null),
                NewToday = g.Count(u => u.CreatedAt >= day),
                NewWeek = g.Count(u => u.CreatedAt >= week),
                NewMonth = g.Count(u => u.CreatedAt >= month),
                LoggedIn30 = g.Count(u => u.LastLoginAt != null && u.LastLoginAt >= month)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new UserActivityStatsDto(
            stats?.Total ?? 0,
            stats?.Dau ?? 0,
            stats?.Wau ?? 0,
            stats?.Mau ?? 0,
            stats?.NeverSeen ?? 0,
            stats?.NewToday ?? 0,
            stats?.NewWeek ?? 0,
            stats?.NewMonth ?? 0,
            stats?.LoggedIn30 ?? 0,
            now);
    }
}
