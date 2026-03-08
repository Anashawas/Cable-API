using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetMyLoyaltyAccount;

public record LoyaltyAccountDto(
    int TotalPointsEarned,
    int TotalPointsRedeemed,
    int CurrentBalance,
    string? CurrentTierName,
    double? CurrentMultiplier,
    int? SeasonPointsEarned,
    string? SeasonName,
    bool IsBlocked,
    DateTime? BlockedUntil,
    string? BlockReason
);

public record GetMyLoyaltyAccountRequest : IRequest<LoyaltyAccountDto>;

public class GetMyLoyaltyAccountRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyLoyaltyAccountRequest, LoyaltyAccountDto>
{
    public async Task<LoyaltyAccountDto> Handle(GetMyLoyaltyAccountRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        if (wallet == null)
            return new LoyaltyAccountDto(0, 0, 0, "Bronze", 1.0, 0, null, false, null, null);

        // Get current season progress
        var activeSeason = await applicationDbContext.LoyaltySeasons
            .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

        string? tierName = "Bronze";
        double? multiplier = 1.0;
        int? seasonPoints = 0;
        string? seasonName = null;

        if (activeSeason != null)
        {
            seasonName = activeSeason.Name;
            var progress = await applicationDbContext.UserSeasonProgresses
                .Include(sp => sp.Tier)
                .FirstOrDefaultAsync(sp => sp.UserId == userId
                                           && sp.LoyaltySeasonId == activeSeason.Id
                                           && !sp.IsDeleted, cancellationToken);

            if (progress != null)
            {
                tierName = progress.Tier.Name;
                multiplier = progress.Tier.Multiplier;
                seasonPoints = progress.SeasonPointsEarned;
            }
        }

        return new LoyaltyAccountDto(
            wallet.TotalPointsEarned,
            wallet.TotalPointsRedeemed,
            wallet.CurrentBalance,
            tierName,
            multiplier,
            seasonPoints,
            seasonName,
            wallet.IsBlocked,
            wallet.BlockedUntil,
            wallet.BlockReason);
    }
}
