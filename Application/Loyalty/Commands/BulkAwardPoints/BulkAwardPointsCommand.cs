using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.BulkAwardPoints;

public record BulkAwardPointsResult(int TargetedUsers, int AwardedUsers, int SkippedBlockedUsers, int PointsAwardedTotal);

/// <summary>
/// J1 — awards points to a user segment (same targeting as SendNotificationByFilter:
/// car type / car model / city, plus current-season tier). Every award is a normal
/// AdminAdjust ledger row carrying the admin actor. Blocked loyalty accounts are
/// skipped. Admin role required.
/// </summary>
public record BulkAwardPointsCommand(
    int? CarTypeId,
    int? CarModelId,
    string? City,
    int? TierId,
    int Points,
    string? Note
) : IRequest<BulkAwardPointsResult>;

public class BulkAwardPointsCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<BulkAwardPointsCommand, BulkAwardPointsResult>
{
    public async Task<BulkAwardPointsResult> Handle(BulkAwardPointsCommand request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var adminId = currentUserService.UserId!.Value;

        // 1. Resolve the target segment (mirrors SendNotificationByFilter).
        var usersQuery = applicationDbContext.UserAccounts
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive);

        if (!string.IsNullOrWhiteSpace(request.City))
            usersQuery = usersQuery.Where(x => x.City != null && x.City.ToLower() == request.City.ToLower());

        if (request.CarTypeId.HasValue || request.CarModelId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.UserCars.Any(uc =>
                !uc.IsDeleted &&
                (!request.CarTypeId.HasValue || uc.CarModel.CarTypeId == request.CarTypeId.Value) &&
                (!request.CarModelId.HasValue || uc.CarModelId == request.CarModelId.Value)));
        }

        if (request.TierId.HasValue)
        {
            usersQuery = usersQuery.Where(u => applicationDbContext.UserSeasonProgresses.Any(sp =>
                sp.UserId == u.Id && !sp.IsDeleted
                && sp.TierLevel == request.TierId.Value
                && sp.Season.IsActive && !sp.Season.IsDeleted));
        }

        var targetUserIds = await usersQuery
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (targetUserIds.Count == 0)
            return new BulkAwardPointsResult(0, 0, 0, 0);

        var now = DateTime.UtcNow;
        var note = string.IsNullOrEmpty(request.Note)
            ? $"Bulk award of {request.Points} points"
            : $"[Bulk] {request.Note}";

        var awarded = 0;
        var skippedBlocked = 0;

        // 2. Award inside one DB transaction; each wallet is row-locked like AdminAdjustPoints.
        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var userId in targetUserIds)
            {
                var wallet = await applicationDbContext.UserLoyaltyAccounts
                    .FromSqlRaw("SELECT * FROM [UserLoyaltyAccount] WITH (UPDLOCK, ROWLOCK) WHERE UserId = {0} AND IsDeleted = 0", userId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (wallet == null)
                {
                    wallet = new UserLoyaltyAccount
                    {
                        UserId = userId,
                        CreatedAt = now,
                        CreatedBy = adminId
                    };
                    applicationDbContext.UserLoyaltyAccounts.Add(wallet);
                    await applicationDbContext.SaveChanges(cancellationToken);
                }
                else if (wallet.IsBlocked && (!wallet.BlockedUntil.HasValue || wallet.BlockedUntil.Value >= now))
                {
                    skippedBlocked++;
                    continue;
                }

                wallet.CurrentBalance += request.Points;
                wallet.TotalPointsEarned += request.Points;
                wallet.ModifiedAt = now;
                wallet.ModifiedBy = adminId;

                applicationDbContext.LoyaltyPointTransactions.Add(new LoyaltyPointTransaction
                {
                    UserLoyaltyAccountId = wallet.Id,
                    TransactionType = (int)TransactionType.AdminAdjust,
                    Points = request.Points,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceType = "BulkAward",
                    Note = note,
                    CreatedAt = now,
                    CreatedBy = adminId
                });

                awarded++;
            }

            await applicationDbContext.SaveChanges(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new BulkAwardPointsResult(
            targetUserIds.Count, awarded, skippedBlocked, awarded * request.Points);
    }
}
