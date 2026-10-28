using Application.Common.Extensions;
using Application.Common.Utilities;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CableGifts.Commands.OrderCableGift;

public record GiftOrderResult(int OrderId, string OrderCode, GiftOrderStatus Status, int PointsSpent, int NewBalance);

/// <summary>
/// POST /api/cable-gifts/{id}/order. Points are taken now; a repeated <see cref="IdempotencyKey"/> returns the
/// first order and never takes points twice.
/// </summary>
public record OrderCableGiftCommand(int GiftId, string? ContactPhone, string? Note, string? IdempotencyKey)
    : IRequest<GiftOrderResult>;

public class OrderCableGiftCommandValidator : AbstractValidator<OrderCableGiftCommand>
{
    public OrderCableGiftCommandValidator()
    {
        RuleFor(x => x.GiftId).GreaterThan(0);
        RuleFor(x => x.ContactPhone).MaximumLength(30);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).MaximumLength(64);
    }
}

public class OrderCableGiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<OrderCableGiftCommand, GiftOrderResult>
{
    public const string PurchaseActionCode = "GIFT_PURCHASE";

    public async Task<GiftOrderResult> Handle(OrderCableGiftCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();

        // A double tap / retry with the same key: answer with the first order, before any rule can say no
        // (the last piece may have been the user's own first order).
        if (key is not null && await FindByKeyAsync(userId, key, cancellationToken) is { } replay)
            return replay;

        var user = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => new { u.Phone, u.City })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotAuthorizedAccessException("User not found");

        var contactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? user.Phone : request.ContactPhone.Trim();
        if (string.IsNullOrWhiteSpace(contactPhone))
            throw new DataValidationException("ContactPhone",
                Lang.Pick("اكتب رقم للتواصل عشان فريق كيبل يوصلك", "Add a contact phone so the Cable team can reach you"));

        var now = DateTime.UtcNow;

        // Stock, the per-user limit and the balance are checked under locks on the gift row and the wallet,
        // so the last piece can't be sold twice and two orders can't spend the same points.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var gift = await db.CableGifts.FindWithLockAsync("CableGift", request.GiftId, cancellationToken)
                       ?? throw new NotFoundException(Lang.Pick("الهدية غير موجودة", $"Gift {request.GiftId} was not found"));

            // Same key racing in a second request: it waited on the gift lock, the first one is committed now.
            if (key is not null && await FindByKeyAsync(userId, key, cancellationToken) is { } raced)
            {
                await transaction.RollbackAsync(cancellationToken);
                return raced;
            }

            var wallet = await db.UserLoyaltyAccounts
                .FromSqlRaw("SELECT * FROM [UserLoyaltyAccount] WITH (UPDLOCK, ROWLOCK) WHERE UserId = {0} AND IsDeleted = 0", userId)
                .FirstOrDefaultAsync(cancellationToken);
            var balance = wallet?.CurrentBalance ?? 0;
            var blocked = wallet is not null && GiftRules.IsLoyaltyBlocked(wallet.IsBlocked, wallet.BlockedUntil, now);

            var myActiveOrders = await db.GiftOrders.CountAsync(o => o.CableGiftId == gift.Id && o.UserId == userId
                                                                     && o.Status != GiftOrderStatus.Cancelled
                                                                     && !o.IsDeleted, cancellationToken);

            var reason = GiftRules.WhyCannotOrder(
                gift.IsActive, gift.StartsAt, gift.EndsAt,
                GiftRules.IsInCity(gift.Cities, JordanCityResolver.Resolve(user.City)),
                gift.PointsCost, gift.Stock, gift.OrdersCount, gift.PerUserLimit,
                balance, myActiveOrders, blocked, now);

            if (reason is not null)
                throw GiftErrorCodes.Error(reason, reason == GiftErrorCodes.InsufficientPoints
                    ? new Dictionary<string, object?>
                    {
                        ["pointsCost"] = gift.PointsCost,
                        ["balance"] = balance,
                        ["pointsMissing"] = GiftRules.PointsMissing(gift.PointsCost, balance)
                    }
                    : null);

            // balance >= PointsCost > 0, so the wallet exists.
            wallet!.CurrentBalance -= gift.PointsCost;
            wallet.TotalPointsRedeemed += gift.PointsCost;
            wallet.ModifiedAt = now;
            wallet.ModifiedBy = userId;

            gift.OrdersCount += 1;

            var order = new GiftOrder
            {
                OrderCode = await NewUniqueOrderCodeAsync(cancellationToken),
                CableGiftId = gift.Id,
                UserId = userId,
                PointsSpent = gift.PointsCost,
                Status = GiftOrderStatus.Pending,
                ContactPhone = contactPhone,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                IdempotencyKey = key
            };
            db.GiftOrders.Add(order);
            await db.SaveChanges(cancellationToken);

            var actionId = await db.LoyaltyPointActions.AsNoTracking()
                .Where(a => a.ActionCode == PurchaseActionCode && !a.IsDeleted)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var seasonId = await db.LoyaltySeasons.AsNoTracking()
                .Where(s => s.IsActive && !s.IsDeleted)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var history = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = wallet.Id,
                LoyaltyPointActionId = actionId,
                LoyaltySeasonId = seasonId,
                TransactionType = (int)TransactionType.Redeem,
                Points = -gift.PointsCost,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceType = "GiftOrder",
                ReferenceId = order.Id,
                // Stored in the buyer's (request) language; the user's history re-renders it in the reader's language.
                Note = GiftHistoryTexts.Purchase(GiftHistoryTexts.RequestLanguage, gift.NameAr, gift.NameEn, order.OrderCode),
                CreatedAt = now,
                CreatedBy = userId
            };
            db.LoyaltyPointTransactions.Add(history);
            await db.SaveChanges(cancellationToken);

            order.PointsTransactionId = history.Id;
            await db.SaveChanges(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new GiftOrderResult(order.Id, order.OrderCode, order.Status, order.PointsSpent, wallet.CurrentBalance);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // The unique (UserId, IdempotencyKey) index caught a same-key race: hand back the winner.
            await transaction.RollbackAsync(cancellationToken);
            return await FindByKeyAsync(userId, key, cancellationToken) ?? throw new CableApplicationException();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<GiftOrderResult?> FindByKeyAsync(int userId, string key, CancellationToken ct)
    {
        var order = await db.GiftOrders.AsNoTracking()
            .Where(o => o.UserId == userId && o.IdempotencyKey == key && !o.IsDeleted)
            .Select(o => new { o.Id, o.OrderCode, o.Status, o.PointsSpent })
            .FirstOrDefaultAsync(ct);
        if (order is null) return null;

        var balance = await db.UserLoyaltyAccounts.AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsDeleted)
            .Select(w => w.CurrentBalance)
            .FirstOrDefaultAsync(ct);
        return new GiftOrderResult(order.Id, order.OrderCode, order.Status, order.PointsSpent, balance);
    }

    private async Task<string> NewUniqueOrderCodeAsync(CancellationToken ct)
    {
        string code;
        do code = GiftRules.NewOrderCode();
        while (await db.GiftOrders.AnyAsync(o => o.OrderCode == code, ct));
        return code;
    }
}
