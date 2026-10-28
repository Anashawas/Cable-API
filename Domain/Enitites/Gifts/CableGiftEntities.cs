using Cable.Core.Enums;
using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A gift from Cable's own catalogue, bought with loyalty points and delivered by the Cable team
/// (Scripts/Sprint5_GiftsAndPromos.sql). Not the old provider rewards (<see cref="LoyaltyReward"/>), which stay as they are.
/// </summary>
public class CableGift : BaseAuditableEntity
{
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>Shown to the user before buying ("we call you to arrange delivery").</summary>
    public string? HowToReceiveAr { get; set; }
    public string? HowToReceiveEn { get; set; }

    public GiftType GiftType { get; set; }

    /// <summary>Always greater than 0.</summary>
    public int PointsCost { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Total pieces; null = unlimited. Remaining = Stock - OrdersCount.</summary>
    public int? Stock { get; set; }

    /// <summary>Orders that hold a piece (Pending + Fulfilled). A cancelled order gives its piece back.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Orders one user may hold (cancelled ones do not count); null = unlimited.</summary>
    public int? PerUserLimit { get; set; }

    /// <summary>UTC; null = from now on.</summary>
    public DateTime? StartsAt { get; set; }

    /// <summary>UTC; null = no end.</summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>Comma-separated canonical city names (Amman,Irbid); null/empty = every city.</summary>
    public string? Cities { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<GiftOrder> Orders { get; set; } = new List<GiftOrder>();
}

/// <summary>One purchase of a <see cref="CableGift"/>. Points are taken when ordered and given back when cancelled.</summary>
public class GiftOrder : BaseAuditableEntity
{
    /// <summary>"GFT-" + 6 characters without 0 O 1 I L.</summary>
    public string OrderCode { get; set; } = null!;
    public int CableGiftId { get; set; }
    public int UserId { get; set; }
    public int PointsSpent { get; set; }
    public GiftOrderStatus Status { get; set; }

    public string? ContactPhone { get; set; }

    /// <summary>The user's own note ("I'll pick it up in Amman").</summary>
    public string? Note { get; set; }

    /// <summary>Admin's note or voucher code, shown to the user once fulfilled.</summary>
    public string? FulfillmentNote { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public int? FulfilledByUserId { get; set; }

    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }

    /// <summary>The app's Idempotency-Key header: a repeated key returns this order instead of buying twice. Unique per user.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>The points-history line of the purchase, and of the refund when cancelled.</summary>
    public int? PointsTransactionId { get; set; }
    public int? RefundTransactionId { get; set; }

    public virtual CableGift Gift { get; set; } = null!;
    public virtual UserAccount User { get; set; } = null!;
}
