namespace Cable.Core.Enums;

// Sprint 5 — Cable Gifts and promo codes (Docs/Sprint5-GiftsAndPromos.md).
// Stored as INT; the API returns the names (JsonStringEnumConverter), e.g. "giftType": "Physical".

/// <summary>How a Cable gift reaches the user. The admin's "how to receive" text says the details.</summary>
public enum GiftType
{
    Physical = 1,
    ChargeCredit = 2,
    Voucher = 3,
    Other = 4
}

/// <summary>Pending → Fulfilled, or Pending → Cancelled (points refunded).</summary>
public enum GiftOrderStatus
{
    Pending = 1,
    Fulfilled = 2,
    Cancelled = 3
}

/// <summary>What a promo does: the first two change the AMOUNT the user pays, the last two the POINTS.</summary>
public enum PromoEffectType
{
    DiscountPercent = 1,
    DiscountFixed = 2,
    PointsMultiplier = 3,
    BonusPoints = 4
}

/// <summary>Which stations / service providers a campaign covers.</summary>
public enum PromoTargetScope
{
    None = 0,
    All = 1,
    Selected = 2
}

/// <summary>Public = one admin-chosen code for many users (CABLE20); Unique = a batch of PRM- codes, one per user.</summary>
public enum PromoCodeMode
{
    Public = 1,
    Unique = 2
}

/// <summary>How a promo got into the user's "My codes".</summary>
public enum UserPromoSource
{
    Code = 1,
    Assigned = 2
}

public enum UserPromoStatus
{
    Available = 1,
    Used = 2,
    Expired = 3,
    Revoked = 4
}

/// <summary>Who an admin assignment goes to.</summary>
public enum PromoAssignTarget
{
    All = 1,
    City = 2,
    Users = 3
}
