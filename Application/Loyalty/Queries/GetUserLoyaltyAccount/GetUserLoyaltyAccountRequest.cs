using Application.Common.Security;
using Application.Loyalty.Queries.GetMyLoyaltyAccount;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetUserLoyaltyAccount;

/// <summary>
/// Admin variant of GetMyLoyaltyAccount: full loyalty snapshot for ANY user.
/// Reuses <see cref="LoyaltyAccountDto"/>. Admin role required.
/// </summary>
public record GetUserLoyaltyAccountRequest(int UserId) : IRequest<LoyaltyAccountDto>;

public class GetUserLoyaltyAccountRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserLoyaltyAccountRequest, LoyaltyAccountDto>
{
    public async Task<LoyaltyAccountDto> Handle(GetUserLoyaltyAccountRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var userExists = await applicationDbContext.UserAccounts
            .AnyAsync(x => x.Id == request.UserId && !x.IsDeleted, cancellationToken);
        if (!userExists)
            throw new NotFoundException($"can not find user with id {request.UserId}");

        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == request.UserId && !w.IsDeleted, cancellationToken);

        if (wallet == null)
            return new LoyaltyAccountDto(0, 0, 0, "Bronze", 1.0, 0, null, false, null, null);

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
                .FirstOrDefaultAsync(sp => sp.UserId == request.UserId
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
