using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.EndSeason;

public record EndSeasonResult(int UsersProcessed, int TotalBonusPointsAwarded);

public record EndSeasonCommand : IRequest<EndSeasonResult>;

public class EndSeasonCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<EndSeasonCommand, EndSeasonResult>
{
    public async Task<EndSeasonResult> Handle(EndSeasonCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        var activeSeason = await applicationDbContext.LoyaltySeasons
                               .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken)
                           ?? throw new NotFoundException("No active season found");

        var now = DateTime.UtcNow;

        // Get all user progress for this season
        var userProgresses = await applicationDbContext.UserSeasonProgresses
            .Include(sp => sp.Tier)
            .Where(sp => sp.LoyaltySeasonId == activeSeason.Id && !sp.IsDeleted)
            .ToListAsync(cancellationToken);

        var totalBonusPoints = 0;

        foreach (var progress in userProgresses)
        {
            var bonusPoints = progress.Tier.BonusPoints;
            if (bonusPoints <= 0) continue;

            // Get wallet
            var wallet = await applicationDbContext.UserLoyaltyAccounts
                .FirstOrDefaultAsync(w => w.UserId == progress.UserId && !w.IsDeleted, cancellationToken);

            if (wallet == null) continue;

            // Award bonus points
            wallet.CurrentBalance += bonusPoints;
            wallet.TotalPointsEarned += bonusPoints;
            wallet.ModifiedAt = now;
            wallet.ModifiedBy = adminId;

            // Create bonus transaction
            var transaction = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = wallet.Id,
                LoyaltySeasonId = activeSeason.Id,
                TransactionType = (int)TransactionType.SeasonBonus,
                Points = bonusPoints,
                BalanceAfter = wallet.CurrentBalance,
                Note = $"Season-end bonus ({progress.Tier.Name} tier) for {activeSeason.Name}",
                CreatedAt = now,
                CreatedBy = adminId
            };
            applicationDbContext.LoyaltyPointTransactions.Add(transaction);

            totalBonusPoints += bonusPoints;
        }

        // Deactivate the season
        activeSeason.IsActive = false;
        activeSeason.ModifiedAt = now;
        activeSeason.ModifiedBy = adminId;

        await applicationDbContext.SaveChanges(cancellationToken);

        return new EndSeasonResult(userProgresses.Count, totalBonusPoints);
    }
}
