using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetUsersSummary;

public record RoleCountDto(int RoleId, string Name, int Count);

public record CityCountDto(string City, int Count);

public record RegistrationDayDto(DateTime Day, int Count);

public record UsersSummaryDto(
    int TotalUsers,
    int Active,
    int Deleted,
    int PhoneVerified,
    int WithVehicles,
    int NewToday,
    int NewThisWeek,
    int NewThisMonth,
    List<RoleCountDto> ByRole,
    List<CityCountDto> ByCity,
    List<RegistrationDayDto> RegistrationTrend
);

/// <summary>
/// A1 — one aggregate call for the admin Users header + insights strip.
/// Counts of non-deleted users unless stated otherwise; date boundaries are UTC.
/// Admin role required.
/// </summary>
public record GetUsersSummaryRequest() : IRequest<UsersSummaryDto>;

public class GetUsersSummaryRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUsersSummaryRequest, UsersSummaryDto>
{
    public async Task<UsersSummaryDto> Handle(GetUsersSummaryRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var users = applicationDbContext.UserAccounts.AsNoTracking();
        var live = users.Where(x => !x.IsDeleted);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek); // Sunday, matching settlement weeks
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var trendStart = today.AddDays(-30);

        var active = await live.CountAsync(cancellationToken);
        var deleted = await users.CountAsync(x => x.IsDeleted, cancellationToken);
        var phoneVerified = await live.CountAsync(x => x.IsPhoneVerified, cancellationToken);
        var withVehicles = await live.CountAsync(x => x.UserCars.Any(c => !c.IsDeleted), cancellationToken);

        var newToday = await live.CountAsync(x => x.CreatedAt >= today, cancellationToken);
        var newThisWeek = await live.CountAsync(x => x.CreatedAt >= weekStart, cancellationToken);
        var newThisMonth = await live.CountAsync(x => x.CreatedAt >= monthStart, cancellationToken);

        // Per-role counts (drives the header tabs — must match the list's counts).
        var byRoleRaw = await live
            .GroupBy(x => new { x.RoleId, x.Role.Name })
            .Select(g => new { g.Key.RoleId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ToListAsync(cancellationToken);
        var byRole = byRoleRaw.Select(r => new RoleCountDto(r.RoleId, r.Name, r.Count)).ToList();

        // Top 10 cities + an "Other" bucket for the rest.
        var cityCounts = await live
            .Where(x => x.City != null && x.City != "")
            .GroupBy(x => x.City!)
            .Select(g => new { City = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(cancellationToken);

        var byCity = cityCounts.Take(10)
            .Select(c => new CityCountDto(c.City, c.Count))
            .ToList();
        var otherCount = cityCounts.Skip(10).Sum(c => c.Count);
        if (otherCount > 0)
            byCity.Add(new CityCountDto("Other", otherCount));

        // Daily registrations, last 30 days.
        var trendRaw = await live
            .Where(x => x.CreatedAt >= trendStart)
            .GroupBy(x => x.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken);

        var trend = trendRaw.Select(d => new RegistrationDayDto(d.Day, d.Count)).ToList();

        return new UsersSummaryDto(
            active,
            active,
            deleted,
            phoneVerified,
            withVehicles,
            newToday,
            newThisWeek,
            newThisMonth,
            byRole,
            byCity,
            trend);
    }
}
