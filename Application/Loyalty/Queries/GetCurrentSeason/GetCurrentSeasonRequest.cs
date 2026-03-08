using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetCurrentSeason;

public record CurrentSeasonDto(
    int? SeasonId,
    string? Name,
    string? Description,
    DateTime? StartDate,
    DateTime? EndDate,
    bool IsActive,
    int? SeasonPointsEarned,
    string? TierName,
    double? Multiplier,
    int? NextTierMinPoints,
    string? NextTierName
);

public record GetCurrentSeasonRequest : IRequest<CurrentSeasonDto>;

public class GetCurrentSeasonRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrentSeasonRequest, CurrentSeasonDto>
{
    public async Task<CurrentSeasonDto> Handle(GetCurrentSeasonRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var activeSeason = await applicationDbContext.LoyaltySeasons
            .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

        if (activeSeason == null)
            return new CurrentSeasonDto(null, null, null, null, null, false, null, null, null, null, null);

        var progress = await applicationDbContext.UserSeasonProgresses
            .Include(sp => sp.Tier)
            .FirstOrDefaultAsync(sp => sp.UserId == userId
                                       && sp.LoyaltySeasonId == activeSeason.Id
                                       && !sp.IsDeleted, cancellationToken);

        var tierName = progress?.Tier.Name ?? "Bronze";
        var multiplier = progress?.Tier.Multiplier ?? 1.0;
        var seasonPoints = progress?.SeasonPointsEarned ?? 0;

        // Find next tier
        var allTiers = await applicationDbContext.LoyaltyTiers
            .Where(t => t.IsActive)
            .OrderBy(t => t.MinPoints)
            .ToListAsync(cancellationToken);

        var currentTierIndex = allTiers.FindIndex(t => t.Id == (progress?.TierLevel ?? 1));
        string? nextTierName = null;
        int? nextTierMinPoints = null;

        if (currentTierIndex >= 0 && currentTierIndex < allTiers.Count - 1)
        {
            var nextTier = allTiers[currentTierIndex + 1];
            nextTierName = nextTier.Name;
            nextTierMinPoints = nextTier.MinPoints;
        }

        return new CurrentSeasonDto(
            activeSeason.Id, activeSeason.Name, activeSeason.Description,
            activeSeason.StartDate, activeSeason.EndDate, true,
            seasonPoints, tierName, multiplier,
            nextTierMinPoints, nextTierName);
    }
}
