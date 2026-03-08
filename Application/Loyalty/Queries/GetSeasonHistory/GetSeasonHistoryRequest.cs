using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetSeasonHistory;

public record SeasonHistoryDto(
    int SeasonId,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    int SeasonPointsEarned,
    string TierName,
    int BonusPointsAwarded
);

public record GetSeasonHistoryRequest : IRequest<List<SeasonHistoryDto>>;

public class GetSeasonHistoryRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetSeasonHistoryRequest, List<SeasonHistoryDto>>
{
    public async Task<List<SeasonHistoryDto>> Handle(GetSeasonHistoryRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        return await applicationDbContext.UserSeasonProgresses
            .Include(sp => sp.Season)
            .Include(sp => sp.Tier)
            .Where(sp => sp.UserId == userId && !sp.IsDeleted && !sp.Season.IsActive)
            .OrderByDescending(sp => sp.Season.EndDate)
            .Select(sp => new SeasonHistoryDto(
                sp.LoyaltySeasonId,
                sp.Season.Name,
                sp.Season.StartDate,
                sp.Season.EndDate,
                sp.SeasonPointsEarned,
                sp.Tier.Name,
                sp.Tier.BonusPoints))
            .ToListAsync(cancellationToken);
    }
}
