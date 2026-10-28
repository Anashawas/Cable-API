using Application.Common.Utilities;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.CableGifts;

/// <summary>One gift as the user app shows it (list card and details). Texts in the request language.</summary>
public record CableGiftDto(
    int Id,
    string Name,
    string? Description,
    string? HowToReceive,
    GiftType GiftType,
    int PointsCost,
    string? ImageUrl,
    /// <summary>Null = unlimited.</summary>
    int? Remaining,
    DateTime? StartsAt,
    DateTime? EndsAt,
    /// <summary>The user's orders that count against the limit (pending + fulfilled).</summary>
    int MyOrdersCount,
    int? PerUserLimit,
    bool CanOrder,
    /// <summary>INSUFFICIENT_POINTS | OUT_OF_STOCK | LIMIT_REACHED | GIFT_EXPIRED | GIFT_NOT_AVAILABLE | LOYALTY_BLOCKED, or null.</summary>
    string? CannotOrderReason,
    /// <summary>Ready-to-show text of <see cref="CannotOrderReason"/>.</summary>
    string? CannotOrderMessage,
    int MyBalance,
    /// <summary>"You need 40 more points"; 0 when the balance covers it.</summary>
    int PointsMissing,
    /// <summary>"Your balance after buying: 140"; null when the user can't afford it.</summary>
    int? BalanceAfterOrder);

/// <summary>One order in "My gifts".</summary>
public record MyGiftOrderDto(
    int OrderId,
    string OrderCode,
    int GiftId,
    string GiftName,
    string? GiftImageUrl,
    GiftType GiftType,
    int PointsSpent,
    GiftOrderStatus Status,
    string? ContactPhone,
    string? Note,
    /// <summary>The admin's note or voucher code — only once fulfilled.</summary>
    string? FulfillmentNote,
    DateTime? FulfilledAt,
    string? CancelReason,
    DateTime? CancelledAt,
    /// <summary>True for a cancelled order: its points were returned.</summary>
    bool PointsRefunded,
    DateTime CreatedAt);

/// <summary>The user's loyalty state the gift rules need.</summary>
public record GiftUserState(int Balance, bool LoyaltyBlocked, string? City);

public static class GiftViews
{
    public static async Task<GiftUserState> LoadUserStateAsync(IApplicationDbContext db, int userId, DateTime nowUtc, CancellationToken ct)
    {
        var wallet = await db.UserLoyaltyAccounts.AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsDeleted)
            .Select(w => new { w.CurrentBalance, w.IsBlocked, w.BlockedUntil })
            .FirstOrDefaultAsync(ct);
        var city = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.City)
            .FirstOrDefaultAsync(ct);

        return new GiftUserState(
            wallet?.CurrentBalance ?? 0,
            wallet is not null && GiftRules.IsLoyaltyBlocked(wallet.IsBlocked, wallet.BlockedUntil, nowUtc),
            JordanCityResolver.Resolve(city));
    }

    /// <summary>Active (non-cancelled) orders of the user per gift.</summary>
    public static async Task<Dictionary<int, int>> MyActiveOrderCountsAsync(IApplicationDbContext db, int userId,
        IReadOnlyCollection<int> giftIds, CancellationToken ct) =>
        giftIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.GiftOrders.AsNoTracking()
                .Where(o => o.UserId == userId && giftIds.Contains(o.CableGiftId)
                            && o.Status != GiftOrderStatus.Cancelled && !o.IsDeleted)
                .GroupBy(o => o.CableGiftId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public static CableGiftDto ToDto(CableGift g, GiftUserState user, int myActiveOrders, DateTime nowUtc)
    {
        var reason = GiftRules.WhyCannotOrder(
            g.IsActive && !g.IsDeleted, g.StartsAt, g.EndsAt, GiftRules.IsInCity(g.Cities, user.City),
            g.PointsCost, g.Stock, g.OrdersCount, g.PerUserLimit,
            user.Balance, myActiveOrders, user.LoyaltyBlocked, nowUtc);

        return new CableGiftDto(
            g.Id,
            Lang.Pick(g.NameAr, g.NameEn),
            PickOptional(g.DescriptionAr, g.DescriptionEn),
            PickOptional(g.HowToReceiveAr, g.HowToReceiveEn),
            g.GiftType,
            g.PointsCost,
            g.ImageUrl,
            GiftRules.Remaining(g.Stock, g.OrdersCount),
            g.StartsAt,
            g.EndsAt,
            myActiveOrders,
            g.PerUserLimit,
            reason is null,
            reason,
            reason is null ? null : GiftErrorCodes.Message(reason),
            user.Balance,
            GiftRules.PointsMissing(g.PointsCost, user.Balance),
            user.Balance >= g.PointsCost ? user.Balance - g.PointsCost : null);
    }

    /// <summary>Request language, falling back to the other language when only one was written.</summary>
    public static string? PickOptional(string? ar, string? en) =>
        Lang.IsArabic ? (string.IsNullOrWhiteSpace(ar) ? en : ar) : (string.IsNullOrWhiteSpace(en) ? ar : en);
}
