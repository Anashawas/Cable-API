using Cable.Core.Enums;

namespace Application.Promos;

/// <summary>Everything a promo changes on one partner transaction — computed by the server only.</summary>
public record PromoQuote(
    decimal OriginalAmountJod,
    decimal DiscountJod,
    decimal FinalAmountJod,
    /// <summary>Paid by Cable to the partner.</summary>
    decimal CableShareJod,
    /// <summary>Carried by the partner.</summary>
    decimal ProviderShareJod,
    decimal CommissionBeforeJod,
    /// <summary>On what the partner actually received: final + Cable's share.</summary>
    decimal CommissionAfterJod,
    /// <summary>Points on the final amount, before any multiplier.</summary>
    int BasePoints,
    /// <summary>What the promo itself adds on top of what the user would get without it (on the final amount).</summary>
    int BonusPoints,
    int FinalPoints,
    /// <summary>The multiplier that applies after the promo (the higher of boost and promo), null when none.</summary>
    double? Multiplier,
    /// <summary>True when the promo multiplier beat the boost / welcome bonus — the boost then does not apply.</summary>
    bool PromoMultiplierWins)
{
    public decimal CommissionRefundJod => CommissionBeforeJod - CommissionAfterJod;
}

/// <summary>
/// Pure promo arithmetic (no database), document §2.9:
/// points on the FINAL amount; promo multiplier vs boost = the HIGHER, never multiplied; bonus points on top;
/// cableShare = discount × cableSharePercent / 100; commission on final + cableShare; amounts rounded to the fils.
/// </summary>
public static class PromoMath
{
    public static decimal RoundJod(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    public static bool IsDiscount(PromoEffectType type) => type is PromoEffectType.DiscountPercent or PromoEffectType.DiscountFixed;

    /// <summary>The discount of one use: never more than the cap, never more than the amount.</summary>
    public static decimal Discount(PromoEffectType type, decimal value, decimal? maxDiscountJod, decimal amountJod)
    {
        if (amountJod <= 0) return 0m;
        var discount = type switch
        {
            PromoEffectType.DiscountPercent => amountJod * Math.Clamp(value, 0m, 100m) / 100m,
            PromoEffectType.DiscountFixed => Math.Max(0m, value),
            _ => 0m
        };
        if (maxDiscountJod is > 0) discount = Math.Min(discount, maxDiscountJod.Value);
        return RoundJod(Math.Min(discount, amountJod));
    }

    /// <summary>(Cable's share, the partner's share) of a discount.</summary>
    public static (decimal Cable, decimal Provider) Split(decimal discountJod, decimal cableSharePercent)
    {
        var cable = RoundJod(discountJod * Math.Clamp(cableSharePercent, 0m, 100m) / 100m);
        return (cable, discountJod - cable);
    }

    /// <summary>Same formula InitiatePartnerTransaction reserves with, so an unchanged base gives the same figure.</summary>
    public static decimal Commission(decimal baseJod, double commissionPercentage) =>
        baseJod * (decimal)(commissionPercentage / 100.0);

    /// <summary>Same formula InitiatePartnerTransaction uses for the code's points.</summary>
    public static int Points(decimal amountJod, double pointsRewardPercentage, double pointsConversionRate)
    {
        var eligible = amountJod * (decimal)(pointsRewardPercentage / 100.0);
        return Math.Max(0, (int)Math.Floor((double)eligible * pointsConversionRate));
    }

    /// <param name="baselinePoints">The code's points before any multiplier (PartnerTransaction.BasePoints) — reused as-is when there is no discount.</param>
    /// <param name="boostMultiplier">The boost / welcome bonus resolved for this scan, null when none.</param>
    public static PromoQuote Quote(
        PromoEffectType effectType, decimal effectValue, decimal? maxDiscountJod, decimal cableSharePercent,
        decimal amountJod, decimal commissionBeforeJod, double commissionPercentage,
        double pointsRewardPercentage, double pointsConversionRate, int baselinePoints, double? boostMultiplier)
    {
        var discount = Discount(effectType, effectValue, maxDiscountJod, amountJod);
        var final = amountJod - discount;
        var (cable, provider) = Split(discount, cableSharePercent);

        // Only the partner's own share lowers what he received; at 100 % Cable share the commission stays as reserved.
        var commissionAfter = provider > 0
            ? Math.Min(commissionBeforeJod, Commission(final + cable, commissionPercentage))
            : commissionBeforeJod;

        var basePoints = discount > 0 ? Points(final, pointsRewardPercentage, pointsConversionRate) : baselinePoints;

        var boost = boostMultiplier ?? 1.0;
        var promoMultiplier = effectType == PromoEffectType.PointsMultiplier ? (double)effectValue : 1.0;
        var promoWins = promoMultiplier > boost;
        var multiplier = promoWins ? promoMultiplier : boost;

        var withoutPromo = (int)Math.Floor(basePoints * boost);
        var multiplied = (int)Math.Floor(basePoints * multiplier);
        var bonus = effectType == PromoEffectType.BonusPoints ? (int)Math.Floor(effectValue) : 0;
        var finalPoints = multiplied + bonus;

        return new PromoQuote(
            amountJod, discount, final, cable, provider,
            commissionBeforeJod, commissionAfter,
            basePoints, Math.Max(0, finalPoints - withoutPromo), finalPoints,
            promoWins ? promoMultiplier : boostMultiplier,
            promoWins);
    }
}
