using Application.Common.Utilities;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.CableGifts;

/// <summary>The 422 codes of the gift endpoints (field "code"); the app switches on them.</summary>
public static class GiftErrorCodes
{
    public const string InsufficientPoints = "INSUFFICIENT_POINTS";
    public const string OutOfStock = "OUT_OF_STOCK";
    public const string LimitReached = "LIMIT_REACHED";
    public const string GiftExpired = "GIFT_EXPIRED";
    public const string GiftNotAvailable = "GIFT_NOT_AVAILABLE";
    public const string LoyaltyBlocked = "LOYALTY_BLOCKED";

    /// <summary>Ready-to-show text in the request language.</summary>
    public static string Message(string code) => code switch
    {
        InsufficientPoints => Lang.Pick("نقاطك ما بتكفي", "You don't have enough points"),
        OutOfStock => Lang.Pick("نفدت الكمية", "This gift is out of stock"),
        LimitReached => Lang.Pick("استبدلت هاي الهدية قبل", "You have already redeemed this gift"),
        GiftExpired => Lang.Pick("انتهى وقت هاي الهدية", "This gift has ended"),
        GiftNotAvailable => Lang.Pick("الهدية غير متاحة حالياً", "This gift is not available right now"),
        LoyaltyBlocked => Lang.Pick("حسابك موقوف عن الولاء مؤقتاً", "Your loyalty account is temporarily suspended"),
        _ => Lang.Pick("ما قدرنا نكمّل الطلب", "The order could not be completed")
    };

    public static UnprocessableException Error(string code, IReadOnlyDictionary<string, object?>? details = null) =>
        new(code, Message(code), details);
}

/// <summary>Pure gift rules (no database) — what the list shows and what the order enforces again under a lock.</summary>
public static class GiftRules
{
    public const string OrderCodePrefix = "GFT";
    public const int OrderCodeLength = 6;

    public static string NewOrderCode() => SafeCode.NewPrefixed(OrderCodePrefix, OrderCodeLength);

    /// <summary>Pieces left; null = unlimited.</summary>
    public static int? Remaining(int? stock, int ordersCount) =>
        stock is null ? null : Math.Max(0, stock.Value - ordersCount);

    /// <summary>Empty gift cities = every city. A user whose city is unknown only sees city-free gifts.</summary>
    public static bool IsInCity(string? giftCities, string? canonicalUserCity)
    {
        var cities = CsvList.Strings(giftCities);
        if (cities.Count == 0) return true;
        return canonicalUserCity is not null && cities.Contains(canonicalUserCity, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the user may order now, and if not the first reason in the order the app explains it.</summary>
    public static string? WhyCannotOrder(
        bool isActive, DateTime? startsAt, DateTime? endsAt, bool inUserCity,
        int pointsCost, int? stock, int ordersCount, int? perUserLimit,
        int balance, int myActiveOrders, bool loyaltyBlocked, DateTime nowUtc)
    {
        if (!isActive || (startsAt.HasValue && startsAt.Value > nowUtc) || !inUserCity)
            return GiftErrorCodes.GiftNotAvailable;
        if (endsAt.HasValue && endsAt.Value <= nowUtc)
            return GiftErrorCodes.GiftExpired;
        if (loyaltyBlocked)
            return GiftErrorCodes.LoyaltyBlocked;
        if (Remaining(stock, ordersCount) is 0)
            return GiftErrorCodes.OutOfStock;
        if (perUserLimit.HasValue && myActiveOrders >= perUserLimit.Value)
            return GiftErrorCodes.LimitReached;
        if (balance < pointsCost)
            return GiftErrorCodes.InsufficientPoints;
        return null;
    }

    /// <summary>"You need 40 more points" — 0 when the balance covers the cost.</summary>
    public static int PointsMissing(int pointsCost, int balance) => Math.Max(0, pointsCost - balance);

    /// <summary>A block whose end date has passed counts as lifted (same rule as LoyaltyPointService).</summary>
    public static bool IsLoyaltyBlocked(bool isBlocked, DateTime? blockedUntil, DateTime nowUtc) =>
        isBlocked && (!blockedUntil.HasValue || blockedUntil.Value >= nowUtc);
}

/// <summary>
/// The points-history lines of a gift order (ReferenceType "GiftOrder"): the purchase (−points) and the refund on
/// cancel (+points). One wording for both the stored Note and the user's history, which re-renders them in the
/// reader's language (bilingual at read), so a purchase and its refund never show in two different languages.
/// </summary>
public static class GiftHistoryTexts
{
    public const string ReferenceType = "GiftOrder";

    public static string Purchase(string? language, string giftNameAr, string giftNameEn, string orderCode) =>
        Lang.Pick(language, $"شراء هدية: {giftNameAr} ({orderCode})", $"Gift purchase: {giftNameEn} ({orderCode})");

    public static string Refund(string? language, string giftNameAr, string giftNameEn, string orderCode) =>
        Lang.Pick(language, $"استرجاع — إلغاء هدية: {giftNameAr} ({orderCode})", $"Refund — gift cancelled: {giftNameEn} ({orderCode})");

    /// <summary>The request language as a code for <see cref="Lang.Pick(string?, string, string)"/>.</summary>
    public static string RequestLanguage => Lang.IsArabic ? "ar" : "en";
}

/// <summary>Push texts for gift orders, in the device's language.</summary>
public static class GiftPushTexts
{
    public const string FulfilledKind = "Fulfilled";
    public const string CancelledKind = "Cancelled";
    public const string DataType = "cable_gift_order";
    public const string NotificationTypeName = "cable_gift_order";

    public static (string Title, string Body) Compose(string kind, string? language, string orderCode,
        string giftNameAr, string giftNameEn, int points) => kind switch
    {
        FulfilledKind => (
            Lang.Pick(language, "تم تسليم هديتك", "Your gift has been delivered"),
            Lang.Pick(language,
                $"طلبك {orderCode} ({giftNameAr}) تم تسليمه. افتح «هداياي» للتفاصيل.",
                $"Order {orderCode} ({giftNameEn}) is fulfilled. Open My gifts for the details.")),
        _ => (
            Lang.Pick(language, "تم إلغاء طلب الهدية", "Gift order cancelled"),
            Lang.Pick(language,
                $"طلبك {orderCode} ({giftNameAr}) ألغي ورجعتلك {points} نقطة.",
                $"Order {orderCode} ({giftNameEn}) was cancelled and {points} points were returned to you."))
    };
}
