using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.AdminAdjustPoints;

public record AdminAdjustPointsCommand(
    int UserId,
    int Points,
    string? Note,
    string? ReasonCode = null
) : IRequest;

public class AdminAdjustPointsCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<AdminAdjustPointsCommand>
{
    public async Task Handle(AdminAdjustPointsCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.Points == 0)
            throw new DataValidationException("Points", "Points adjustment cannot be zero");

        if (!string.IsNullOrEmpty(request.ReasonCode) && !AdjustmentReasons.IsValid(request.ReasonCode))
            throw new DataValidationException("ReasonCode",
                $"Unknown reason code. Valid codes: {string.Join(", ", AdjustmentReasons.All.Select(r => r.Code))}");

        var now = DateTime.UtcNow;

        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Find or create wallet with row lock
            var wallet = await applicationDbContext.UserLoyaltyAccounts
                .FromSqlRaw("SELECT * FROM [UserLoyaltyAccount] WITH (UPDLOCK, ROWLOCK) WHERE UserId = {0} AND IsDeleted = 0", request.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (wallet == null)
            {
                wallet = new UserLoyaltyAccount
                {
                    UserId = request.UserId,
                    TotalPointsEarned = 0,
                    TotalPointsRedeemed = 0,
                    CurrentBalance = 0,
                    CreatedAt = now,
                    CreatedBy = adminId
                };
                applicationDbContext.UserLoyaltyAccounts.Add(wallet);
                await applicationDbContext.SaveChanges(cancellationToken);
            }

            // Validate negative adjustment doesn't go below zero
            if (request.Points < 0 && wallet.CurrentBalance + request.Points < 0)
                throw new DataValidationException("Points", $"Cannot deduct {Math.Abs(request.Points)} points. User only has {wallet.CurrentBalance} points");

            // Update wallet
            wallet.CurrentBalance += request.Points;
            if (request.Points > 0)
                wallet.TotalPointsEarned += request.Points;

            // Create transaction
            var transaction = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = wallet.Id,
                TransactionType = (int)TransactionType.AdminAdjust,
                Points = request.Points,
                BalanceAfter = wallet.CurrentBalance,
                Note = string.IsNullOrEmpty(request.ReasonCode)
                    ? request.Note ?? $"Admin adjustment of {request.Points} points"
                    : $"[{request.ReasonCode}] {request.Note ?? $"Admin adjustment of {request.Points} points"}",
                CreatedAt = now,
                CreatedBy = adminId
            };
            applicationDbContext.LoyaltyPointTransactions.Add(transaction);

            await applicationDbContext.SaveChanges(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
