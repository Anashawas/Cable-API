using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetLeaderboard;

public record LeaderboardEntryDto(
    int Rank,
    int UserId,
    string? UserName,
    int SeasonPointsEarned,
    string TierName
);

public record GetLeaderboardRequest(int Top = 10) : IRequest<List<LeaderboardEntryDto>>;

public class GetLeaderboardRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetLeaderboardRequest, List<LeaderboardEntryDto>>
{
    public async Task<List<LeaderboardEntryDto>> Handle(GetLeaderboardRequest request, CancellationToken cancellationToken)
    {
        var activeSeason = await applicationDbContext.LoyaltySeasons
            .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

        if (activeSeason == null)
            return new List<LeaderboardEntryDto>();

        var entries = await applicationDbContext.UserSeasonProgresses
            .Include(sp => sp.User)
            .Include(sp => sp.Tier)
            .Where(sp => sp.LoyaltySeasonId == activeSeason.Id && !sp.IsDeleted)
            .OrderByDescending(sp => sp.SeasonPointsEarned)
            .Take(request.Top)
            .Select(sp => new { sp.User.Id, sp.User.Name, sp.SeasonPointsEarned, TierName = sp.Tier.Name })
            .ToListAsync(cancellationToken);

        return entries.Select((e, index) => new LeaderboardEntryDto(
            index + 1, e.Id, e.Name, e.SeasonPointsEarned, e.TierName)).ToList();
    }
}
