using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.AdminAdjustPoints;

public record AdminAdjustPointsCommand(
    int UserId,
    int Points,
    string? Note
) : IRequest;

public class AdminAdjustPointsCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<AdminAdjustPointsCommand>
{
    public async Task Handle(AdminAdjustPointsCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.Points == 0)
            throw new DataValidationException("Points", "Points adjustment cannot be zero");

        // Find or create wallet
        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == request.UserId && !w.IsDeleted, cancellationToken);

        var now = DateTime.UtcNow;

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
            Note = request.Note ?? $"Admin adjustment of {request.Points} points",
        };
        applicationDbContext.LoyaltyPointTransactions.Add(transaction);

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
