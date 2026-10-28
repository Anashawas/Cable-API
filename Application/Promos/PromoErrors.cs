using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.Promos;

/// <summary>The 422 codes of the promo endpoints (field "code"), with ready-to-show texts in the request language.</summary>
public static class PromoErrorCodes
{
    // claim
    public const string NotFound = "PROMO_NOT_FOUND";
    public const string Expired = "PROMO_EXPIRED";
    public const string AlreadyClaimed = "PROMO_ALREADY_CLAIMED";
    public const string LimitReached = "PROMO_LIMIT_REACHED";
    public const string NotEligible = "PROMO_NOT_ELIGIBLE";
    public const string NotStarted = "PROMO_NOT_STARTED";
    public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";

    // scan
    public const string NotApplicable = "PROMO_NOT_APPLICABLE";
    public const string AlreadyUsed = "PROMO_ALREADY_USED";
    public const string BudgetExhausted = "PROMO_BUDGET_EXHAUSTED";

    public static string Message(string code) => code switch
    {
        NotFound => Lang.Pick("هذا الكود غير صحيح", "This code is not valid"),
        Expired => Lang.Pick("هذا الكود منتهي الصلاحية", "This code has expired"),
        AlreadyClaimed => Lang.Pick("الكود موجود عندك بأكوادي", "This code is already in My codes"),
        LimitReached => Lang.Pick("انتهت الكمية المتاحة من هذا العرض", "This offer has run out"),
        NotEligible => Lang.Pick("هذا العرض غير متاح لحسابك", "This offer isn't available for your account"),
        NotStarted => Lang.Pick("العرض لسا ما بلّش", "This offer hasn't started yet"),
        TooManyAttempts => Lang.Pick("محاولات كثيرة غلط. جرّب بعد ساعة", "Too many wrong codes. Please try again in an hour"),
        NotApplicable => Lang.Pick("هذا الكود ما بينطبق على هاي العملية", "This code doesn't apply to this transaction"),
        AlreadyUsed => Lang.Pick("هذا الكود مستعمل", "This code has already been used"),
        BudgetExhausted => Lang.Pick("انتهى رصيد هذا العرض", "This offer's balance has run out"),
        _ => Lang.Pick("ما قدرنا نطبّق الكود", "The code could not be applied")
    };

    public static UnprocessableException Error(string code, IReadOnlyDictionary<string, object?>? details = null) =>
        new(code, Message(code), details);
}

/// <summary>Why the server switched a campaign off (PromoCampaign.DeactivatedReason).</summary>
public static class PromoDeactivationReasons
{
    public const string BudgetExhausted = "BudgetExhausted";
}

/// <summary>Push texts of promos, in the device's language.</summary>
public static class PromoPushTexts
{
    public const string AssignedType = "promo_assigned";
    public const string ExpiringType = "promo_expiring";

    public static (string Title, string Body) Assigned(string? language, string titleAr, string titleEn) => (
        Lang.Pick(language, "عندك كود جديد من كيبل", "A new code from Cable"),
        Lang.Pick(language, $"«{titleAr}» صار بأكوادي. استعمله بمسحتك الجاية.", $"\"{titleEn}\" is now in My codes. Use it on your next scan."));

    public static (string Title, string Body) Expiring(string? language, string titleAr, string titleEn) => (
        Lang.Pick(language, "كودك بينتهي قريباً", "Your code expires soon"),
        Lang.Pick(language, $"«{titleAr}» بينتهي خلال يومين. استعمله قبل ما يروح.", $"\"{titleEn}\" expires within two days. Use it before it's gone."));
}
