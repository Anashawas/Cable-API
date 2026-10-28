using Application.Common.Utilities;
using Cable.Core.Enums;
using Cable.Core.Utilities;

namespace Application.Promos;

/// <summary>The transaction a promo is tested against. City is the provider's canonical city.</summary>
public record PromoTransactionContext(
    string TransactionKind,
    string ProviderType,
    int ProviderId,
    int? ServiceCategoryId,
    string? ProviderCity,
    decimal AmountJod);

/// <summary>Pure promo rules (no database): code format, claim checks, where a campaign applies, expiry, admin validation.</summary>
public static class PromoRules
{
    public const string PartnerScan = "PartnerScan";
    public const string ChargingPoint = "ChargingPoint";
    public const string ServiceProvider = "ServiceProvider";

    public const string UniqueCodePrefix = "PRM";
    public const int UniqueCodeLength = 8;

    /// <summary>"New user" = account created within this many days.</summary>
    public const int NewUserWindowDays = 30;

    /// <summary>Wrong codes allowed per user per hour before "Add code" refuses.</summary>
    public const int MaxWrongAttemptsPerHour = 10;

    /// <summary>The "expires soon" warning / push window.</summary>
    public static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(2);

    public static string NewUniqueCode() => SafeCode.NewPrefixed(UniqueCodePrefix, UniqueCodeLength);

    public static bool IsNewUser(DateTime accountCreatedAtUtc, DateTime nowUtc) =>
        accountCreatedAtUtc >= nowUtc.AddDays(-NewUserWindowDays);

    public static bool IsBudgetExhausted(decimal? budgetJod, decimal spentJod) =>
        budgetJod.HasValue && spentJod >= budgetJod.Value;

    /// <summary>A user's promo expires at the campaign end or N days after it was added, whichever is first.</summary>
    public static DateTime? ExpiryFor(DateTime? campaignEndsAt, int? validDaysAfterClaim, DateTime claimedAtUtc)
    {
        DateTime? byDays = validDaysAfterClaim is > 0 ? claimedAtUtc.AddDays(validDaysAfterClaim.Value) : null;
        if (campaignEndsAt is null) return byDays;
        if (byDays is null) return campaignEndsAt;
        return byDays < campaignEndsAt ? byDays : campaignEndsAt;
    }

    /// <summary>Available promos past their date read as Expired even before the daily job has run.</summary>
    public static UserPromoStatus EffectiveStatus(UserPromoStatus stored, DateTime? expiresAt, DateTime nowUtc) =>
        stored == UserPromoStatus.Available && expiresAt.HasValue && expiresAt.Value <= nowUtc
            ? UserPromoStatus.Expired
            : stored;

    /// <summary>Whether a campaign can be added by code right now (the per-user checks need the database).</summary>
    public static string? WhyCannotClaim(PromoCampaign c, bool isNewUser, DateTime nowUtc)
    {
        if (c.IsDeleted) return PromoErrorCodes.NotFound;
        if (c.EndsAt.HasValue && c.EndsAt.Value <= nowUtc) return PromoErrorCodes.Expired;
        if (!c.IsActive)
            return c.DeactivatedReason == PromoDeactivationReasons.BudgetExhausted
                ? PromoErrorCodes.LimitReached
                : PromoErrorCodes.NotFound;
        if (c.StartsAt.HasValue && c.StartsAt.Value > nowUtc) return PromoErrorCodes.NotStarted;
        if (IsBudgetExhausted(c.CableBudgetJod, c.CableSpentJod)) return PromoErrorCodes.LimitReached;
        if (c.TotalLimit.HasValue && c.ClaimsCount >= c.TotalLimit.Value) return PromoErrorCodes.LimitReached;
        if (c.NewUsersOnly && !isNewUser) return PromoErrorCodes.NotEligible;
        return null;
    }

    /// <summary>
    /// Does the campaign cover this transaction? The partner must be inside the scope of its own type (stations or
    /// service providers); then category (service providers only — a station is in no category), city and minimum amount.
    /// </summary>
    public static bool Covers(PromoCampaign c, PromoTransactionContext tx)
    {
        if (!CsvList.Strings(c.AppliesTo).Contains(tx.TransactionKind, StringComparer.OrdinalIgnoreCase))
            return false;

        var inScope = tx.ProviderType switch
        {
            ChargingPoint => InScope(c.StationsScope, c.StationIds, tx.ProviderId),
            ServiceProvider => InScope(c.ServiceProvidersScope, c.ServiceProviderIds, tx.ProviderId),
            _ => false
        };
        if (!inScope) return false;

        var categories = CsvList.Ints(c.ServiceCategoryIds);
        if (categories.Count > 0 && (tx.ProviderType != ServiceProvider || tx.ServiceCategoryId is null
                                     || !categories.Contains(tx.ServiceCategoryId.Value)))
            return false;

        var cities = CsvList.Strings(c.Cities);
        if (cities.Count > 0 && (tx.ProviderCity is null || !cities.Contains(tx.ProviderCity, StringComparer.OrdinalIgnoreCase)))
            return false;

        return !c.MinAmountJod.HasValue || tx.AmountJod >= c.MinAmountJod.Value;
    }

    private static bool InScope(PromoTargetScope scope, string? ids, int providerId) => scope switch
    {
        PromoTargetScope.All => true,
        PromoTargetScope.Selected => CsvList.Ints(ids).Contains(providerId),
        _ => false
    };

    /// <summary>
    /// Why this user promo can't be used on this transaction, or null when it can. <paramref name="cableShareJod"/>
    /// is Cable's share of THIS use (from <see cref="PromoMath.Quote"/>), checked against the remaining budget.
    /// </summary>
    public static string? WhyNotApplicable(PromoCampaign c, UserPromoStatus storedStatus, DateTime? expiresAt,
        PromoTransactionContext tx, decimal cableShareJod, DateTime nowUtc)
    {
        switch (EffectiveStatus(storedStatus, expiresAt, nowUtc))
        {
            case UserPromoStatus.Used: return PromoErrorCodes.AlreadyUsed;
            case UserPromoStatus.Expired: return PromoErrorCodes.Expired;
            case UserPromoStatus.Revoked: return PromoErrorCodes.NotApplicable;
        }

        if (c.IsDeleted) return PromoErrorCodes.NotApplicable;
        if (c.EndsAt.HasValue && c.EndsAt.Value <= nowUtc) return PromoErrorCodes.Expired;
        if (!c.IsActive)
            return c.DeactivatedReason == PromoDeactivationReasons.BudgetExhausted
                ? PromoErrorCodes.BudgetExhausted
                : PromoErrorCodes.NotApplicable;
        if (c.StartsAt.HasValue && c.StartsAt.Value > nowUtc) return PromoErrorCodes.NotApplicable;
        if (!Covers(c, tx)) return PromoErrorCodes.NotApplicable;

        if (c.CableBudgetJod.HasValue && cableShareJod > 0 && c.CableSpentJod + cableShareJod > c.CableBudgetJod.Value)
            return PromoErrorCodes.BudgetExhausted;

        return null;
    }

    /// <summary>The "where it applies" line of a card: the admin's text, else a generic one.</summary>
    public static string ScopeText(PromoCampaign c)
    {
        var custom = Lang.IsArabic ? c.ScopeLabelAr ?? c.ScopeLabelEn : c.ScopeLabelEn ?? c.ScopeLabelAr;
        if (!string.IsNullOrWhiteSpace(custom)) return custom;

        var stations = c.StationsScope;
        var providers = c.ServiceProvidersScope;
        if (stations == PromoTargetScope.All && providers == PromoTargetScope.All)
            return Lang.Pick("بكل المحطات ومزوّدي الخدمة", "At every station and service provider");
        if (stations == PromoTargetScope.All && providers == PromoTargetScope.None)
            return Lang.Pick("بكل المحطات", "At every station");
        if (providers == PromoTargetScope.All && stations == PromoTargetScope.None)
            return Lang.Pick("عند كل مزوّدي الخدمة", "At every service provider");
        return Lang.Pick("عند شركاء مختارين", "At selected partners");
    }

    public static string Title(PromoCampaign c) => Lang.Pick(c.TitleAr, c.TitleEn);

    public static string? Description(PromoCampaign c) =>
        Lang.IsArabic ? c.DescriptionAr ?? c.DescriptionEn : c.DescriptionEn ?? c.DescriptionAr;

    /// <summary>
    /// Admin form rules that need no database (§2.3–2.5). Returns field → messages; empty when valid.
    /// Key rule: when the partner carries part of a discount (cableSharePercent &lt; 100) both scopes must be
    /// None or Selected — never All: a partner never carries a discount he did not agree to.
    /// </summary>
    public static Dictionary<string, string[]> Validate(
        PromoEffectType effectType, decimal effectValue, decimal? maxDiscountJod,
        decimal cableSharePercent, decimal? cableBudgetJod,
        PromoTargetScope stationsScope, IReadOnlyCollection<int> stationIds,
        PromoTargetScope providersScope, IReadOnlyCollection<int> providerIds,
        IReadOnlyCollection<string> appliesTo, DateTime? startsAt, DateTime? endsAt,
        int? validDaysAfterClaim, int? totalLimit, int? perUserLimit, decimal? minAmountJod,
        PromoCodeMode codeMode, string? publicCodeNormalized)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string key, string message) => errors[key] = [message];

        if (!Enum.IsDefined(effectType)) Add("EffectType", "Unknown effect type");
        switch (effectType)
        {
            case PromoEffectType.DiscountPercent when effectValue is < 1 or > 100:
                Add("EffectValue", "A percent discount is 1–100"); break;
            case PromoEffectType.DiscountFixed when effectValue <= 0:
                Add("EffectValue", "A fixed discount must be more than 0 JOD"); break;
            case PromoEffectType.PointsMultiplier when effectValue is <= 1 or > 10:
                Add("EffectValue", "A points multiplier is above 1 and at most 10 (e.g. 1.5, 2, 3)"); break;
            case PromoEffectType.BonusPoints when effectValue < 1 || effectValue != Math.Floor(effectValue):
                Add("EffectValue", "Bonus points must be a whole number of at least 1"); break;
        }

        if (cableSharePercent is < 0 or > 100) Add("CableSharePercent", "Cable's share is 0–100");
        if (maxDiscountJod is <= 0) Add("MaxDiscountJod", "The discount cap must be more than 0");
        if (cableBudgetJod is <= 0) Add("CableBudgetJod", "The budget must be more than 0");
        if (minAmountJod is < 0) Add("MinAmountJod", "The minimum amount can't be negative");

        if (PromoMath.IsDiscount(effectType) && cableSharePercent > 0)
        {
            // The two mandatory protections whenever Cable pays part of a discount.
            if (cableBudgetJod is null) Add("CableBudgetJod", "A budget is required when Cable pays part of the discount");
            if (effectType == PromoEffectType.DiscountPercent && maxDiscountJod is null)
                Add("MaxDiscountJod", "A per-use discount cap is required when Cable pays part of a percent discount");
        }

        if (PromoMath.IsDiscount(effectType) && cableSharePercent < 100)
        {
            if (stationsScope == PromoTargetScope.All)
                Add("StationsScope", "When the partner pays part of the discount (cableSharePercent < 100) the stations must be Selected, not All");
            if (providersScope == PromoTargetScope.All)
                Add("ServiceProvidersScope", "When the partner pays part of the discount (cableSharePercent < 100) the service providers must be Selected, not All");
        }

        if (!Enum.IsDefined(stationsScope)) Add("StationsScope", "Unknown scope");
        if (!Enum.IsDefined(providersScope)) Add("ServiceProvidersScope", "Unknown scope");
        if (stationsScope == PromoTargetScope.None && providersScope == PromoTargetScope.None)
            Add("StationsScope", "Pick stations, service providers or both");
        if (stationsScope == PromoTargetScope.Selected && stationIds.Count == 0)
            Add("StationIds", "Select at least one station");
        if (providersScope == PromoTargetScope.Selected && providerIds.Count == 0)
            Add("ServiceProviderIds", "Select at least one service provider");

        if (appliesTo.Count == 0 || appliesTo.Any(a => !string.Equals(a, PartnerScan, StringComparison.OrdinalIgnoreCase)))
            Add("AppliesTo", "Version 1 supports PartnerScan only");

        if (startsAt.HasValue && endsAt.HasValue && endsAt <= startsAt) Add("EndsAt", "EndsAt must be after StartsAt");
        if (validDaysAfterClaim is < 1 or > 365) Add("ValidDaysAfterClaim", "Valid days after adding is 1–365");
        if (totalLimit is < 1) Add("TotalLimit", "The total limit must be at least 1");
        if (perUserLimit is < 1 or > 100) Add("PerUserLimit", "The per-user limit is 1–100");

        if (!Enum.IsDefined(codeMode)) Add("CodeMode", "Unknown code mode");
        if (codeMode == PromoCodeMode.Public)
        {
            if (string.IsNullOrEmpty(publicCodeNormalized) || publicCodeNormalized.Length is < 3 or > 30
                || !publicCodeNormalized.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                Add("PublicCode", "The public code is 3–30 letters or digits (spaces and dashes are ignored)");
            else if (publicCodeNormalized.StartsWith(UniqueCodePrefix) || publicCodeNormalized.StartsWith("PTR")
                     || publicCodeNormalized.StartsWith("GFT") || publicCodeNormalized.StartsWith("CBL"))
                Add("PublicCode", "Codes starting with PRM, PTR, GFT or CBL are reserved");
        }

        return errors;
    }
}
