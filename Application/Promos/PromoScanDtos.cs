using Cable.Core.Enums;

namespace Application.Promos;

/// <summary>A promo of the user that applies to the scanned code (PreviewPartnerCode.eligiblePromos).</summary>
public record EligiblePromoDto(
    int UserPromoId,
    string Title,
    PromoEffectType EffectType,
    decimal EffectValue,
    decimal? MaxDiscountJod,
    DateTime? ExpiresAt,
    /// <summary>What this promo would take off this transaction.</summary>
    decimal DiscountJod,
    decimal FinalAmountJod,
    /// <summary>Points this promo would add.</summary>
    int BonusPoints);

/// <summary>PreviewPartnerCode with userPromoId: the computed promo. Points are on the response's top-level fields.</summary>
public record PreviewPromoDto(
    int UserPromoId,
    string Title,
    PromoEffectType EffectType,
    decimal DiscountJod,
    decimal FinalAmountJod,
    int BonusPoints);

/// <summary>The user's ScanPartnerCode result: "you saved 4.000 JOD with Free wash". No Cable / partner split here.</summary>
public record ScanPromoDto(
    int UserPromoId,
    string Title,
    decimal DiscountJod,
    decimal FinalAmountJod,
    int BonusPoints);

/// <summary>
/// The partner's view of a completed transaction with a promo: "collect 0.000 from the customer · Cable pays you 4.000".
/// <see cref="FinalAmountJod"/> is what the partner collects; <see cref="CableShareJod"/> is what Cable owes him.
/// </summary>
public record PartnerTransactionPromoDto(
    string Title,
    decimal OriginalAmountJod,
    decimal DiscountJod,
    decimal FinalAmountJod,
    decimal CableShareJod,
    decimal ProviderShareJod);

/// <summary>A promo applied by a scan, before the transaction commits.</summary>
public record AppliedPromo(PromoUsage Usage, string Title)
{
    public ScanPromoDto ToUserDto() =>
        new(Usage.UserPromoId, Title, Usage.DiscountJod, Usage.FinalAmountJod, Usage.BonusPoints);

    public PartnerTransactionPromoDto ToPartnerDto() =>
        new(Title, Usage.OriginalAmountJod, Usage.DiscountJod, Usage.FinalAmountJod, Usage.CableShareJod, Usage.ProviderShareJod);
}

/// <summary>PreviewPartnerCode.PointsUnavailableReason added by promos.</summary>
public static class PromoPointsReasons
{
    /// <summary>A 100 % discount: nothing paid, so no points from the transaction (bonus points still count).</summary>
    public const string FullyDiscounted = "fullyDiscounted";
}
