using Application.Common.Security;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetUpcomingExpiries;

public record UserExpiryDto(int UserId, string? UserName, int PointsExpiring, DateTime EarliestExpiry);

public record UpcomingExpiriesDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalPointsExpiring,
    int UsersAffected,
    List<UserExpiryDto> Users
);

/// <summary>
/// H2 (visibility) — points that will expire in the window (default: next 90 days),
/// totalled and broken down per user for reminder campaigns.
/// NOTE: earn transactions only carry an ExpiresAt when an expiry policy stamps
/// one at earn time — today no policy is configured, so this reports whatever
/// expiry dates exist in the ledger. Admin role required.
/// </summary>
public record GetUpcomingExpiriesRequest(
    DateTime? From = null,
    DateTime? To = null
) : IRequest<UpcomingExpiriesDto>;

public class GetUpcomingExpiriesRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUpcomingExpiriesRequest, UpcomingExpiriesDto>
{
    public async Task<UpcomingExpiriesDto> Handle(GetUpcomingExpiriesRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var from = request.From ?? DateTime.UtcNow;
        var to = request.To ?? from.AddDays(90);

        var rows = await applicationDbContext.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted
                        && t.TransactionType == (int)TransactionType.Earn
                        && t.ExpiresAt != null
                        && t.ExpiresAt >= from
                        && t.ExpiresAt <= to)
            .GroupBy(t => new { t.Account.UserId, t.Account.User.Name })
            .Select(g => new
            {
                g.Key.UserId,
                g.Key.Name,
                Points = g.Sum(t => t.Points),
                Earliest = g.Min(t => t.ExpiresAt!.Value)
            })
            .OrderByDescending(x => x.Points)
            .ToListAsync(cancellationToken);

        var users = rows
            .Select(x => new UserExpiryDto(x.UserId, x.Name, x.Points, x.Earliest))
            .ToList();

        return new UpcomingExpiriesDto(
            from, to,
            users.Sum(u => u.PointsExpiring),
            users.Count,
            users);
    }
}
