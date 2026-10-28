using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Application.CableGifts.Commands.ManageOrders;

/// <summary>PATCH /api/admin/cable-gifts/orders/{id}/fulfill — the note / voucher code is shown to the user in "My gifts".</summary>
public record FulfillGiftOrderCommand(int Id, string? FulfillmentNote) : IRequest;

public class FulfillGiftOrderCommandValidator : AbstractValidator<FulfillGiftOrderCommand>
{
    public FulfillGiftOrderCommandValidator() => RuleFor(x => x.FulfillmentNote).MaximumLength(1000);
}

public class FulfillGiftOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<FulfillGiftOrderCommand>
{
    public async Task Handle(FulfillGiftOrderCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var order = await db.GiftOrders.FirstOrDefaultAsync(o => o.Id == request.Id && !o.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Gift order {request.Id} was not found");

        if (order.Status != GiftOrderStatus.Pending)
            throw new ConflictException("ORDER_NOT_PENDING", $"Only pending orders can be fulfilled (this one is {order.Status})");

        order.Status = GiftOrderStatus.Fulfilled;
        order.FulfillmentNote = string.IsNullOrWhiteSpace(request.FulfillmentNote) ? null : request.FulfillmentNote.Trim();
        order.FulfilledAt = DateTime.UtcNow;
        order.FulfilledByUserId = currentUser.UserId;
        await db.SaveChanges(cancellationToken);

        var orderId = order.Id;
        jobs.Enqueue<IGiftsAndPromosJobs>(j => j.NotifyGiftOrderAsync(orderId, GiftPushTexts.FulfilledKind, CancellationToken.None));
    }
}

/// <summary>
/// PATCH /api/admin/cable-gifts/orders/{id}/cancel — pending orders only. All points go back to the wallet with a
/// "gift refund" line in the points history, and the piece returns to stock.
/// </summary>
public record CancelGiftOrderCommand(int Id, string? Reason) : IRequest;

public class CancelGiftOrderCommandValidator : AbstractValidator<CancelGiftOrderCommand>
{
    public CancelGiftOrderCommandValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public class CancelGiftOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<CancelGiftOrderCommand>
{
    public const string RefundActionCode = "GIFT_REFUND";

    public async Task Handle(CancelGiftOrderCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var adminId = currentUser.UserId!.Value;
        var now = DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await db.GiftOrders
                            .FromSqlRaw("SELECT * FROM [GiftOrder] WITH (UPDLOCK, ROWLOCK) WHERE Id = {0} AND IsDeleted = 0", request.Id)
                            .Include(o => o.Gift)
                            .FirstOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException($"Gift order {request.Id} was not found");

            if (order.Status != GiftOrderStatus.Pending)
                throw new ConflictException("ORDER_NOT_PENDING", $"Only pending orders can be cancelled (this one is {order.Status})");

            var wallet = await db.UserLoyaltyAccounts
                             .FromSqlRaw("SELECT * FROM [UserLoyaltyAccount] WITH (UPDLOCK, ROWLOCK) WHERE UserId = {0} AND IsDeleted = 0", order.UserId)
                             .FirstOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("The user's loyalty account was not found");

            wallet.CurrentBalance += order.PointsSpent;
            wallet.TotalPointsRedeemed = Math.Max(0, wallet.TotalPointsRedeemed - order.PointsSpent);
            wallet.ModifiedAt = now;
            wallet.ModifiedBy = adminId;

            // The history line is the user's, not the admin's: write it in the user's device language.
            var language = await db.NotificationTokens.AsNoTracking()
                .Where(t => t.UserId == order.UserId && t.AppType == FirebaseAppType.UserApp)
                .OrderByDescending(t => t.UpdatedAt)
                .Select(t => t.Language)
                .FirstOrDefaultAsync(cancellationToken);

            var actionId = await db.LoyaltyPointActions.AsNoTracking()
                .Where(a => a.ActionCode == RefundActionCode && !a.IsDeleted)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var refund = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = wallet.Id,
                LoyaltyPointActionId = actionId,
                TransactionType = (int)TransactionType.AdminAdjust,
                Points = order.PointsSpent,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceType = "GiftOrder",
                ReferenceId = order.Id,
                // Stored in the user's language (never the admin's); the user's history re-renders it in the reader's language.
                Note = GiftHistoryTexts.Refund(language, order.Gift.NameAr, order.Gift.NameEn, order.OrderCode),
                CreatedAt = now,
                CreatedBy = adminId
            };
            db.LoyaltyPointTransactions.Add(refund);

            order.Status = GiftOrderStatus.Cancelled;
            order.CancelReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            order.CancelledAt = now;
            order.CancelledByUserId = adminId;
            await db.SaveChanges(cancellationToken);

            order.RefundTransactionId = refund.Id;

            // The piece goes back to stock.
            await db.CableGifts
                .Where(g => g.Id == order.CableGiftId && g.OrdersCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.OrdersCount, g => g.OrdersCount - 1), cancellationToken);

            await db.SaveChanges(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var orderId = request.Id;
        jobs.Enqueue<IGiftsAndPromosJobs>(j => j.NotifyGiftOrderAsync(orderId, GiftPushTexts.CancelledKind, CancellationToken.None));
    }
}
