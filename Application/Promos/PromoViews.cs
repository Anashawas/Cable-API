using Cable.Core.Enums;

namespace Application.Promos;

/// <summary>One card in "My codes" (also the claim response). Every figure comes from the server.</summary>
public record MyPromoDto(
    int UserPromoId,
    int CampaignId,
    string Title,
    string? Description,
    PromoEffectType EffectType,
    decimal EffectValue,
    decimal? MaxDiscountJod,
    /// <summary>"Where it applies" line.</summary>
    string Scope,
    UserPromoStatus Status,
    UserPromoSource Source,
    DateTime ClaimedAt,
    DateTime? ExpiresAt,
    /// <summary>Whole days left (rounded up); null without an expiry or when no longer available.</summary>
    int? ExpiresInDays,
    /// <summary>True within the last two days — the app shows "expires in 2 days".</summary>
    bool ExpiringSoon,
    // Used promos only:
    DateTime? UsedAt,
    string? UsedAtProviderName,
    decimal? SavedJod,
    int? BonusPoints);

public static class PromoViews
{
    public static MyPromoDto ToDto(UserPromo up, PromoCampaign c, DateTime nowUtc,
        PromoUsage? usage = null, string? providerName = null)
    {
        var status = PromoRules.EffectiveStatus(up.Status, up.ExpiresAt, nowUtc);
        var available = status == UserPromoStatus.Available;
        int? daysLeft = available && up.ExpiresAt.HasValue
            ? Math.Max(0, (int)Math.Ceiling((up.ExpiresAt.Value - nowUtc).TotalDays))
            : null;

        return new MyPromoDto(
            up.Id, c.Id, PromoRules.Title(c), PromoRules.Description(c),
            c.EffectType, c.EffectValue, c.MaxDiscountJod, PromoRules.ScopeText(c),
            status, up.Source, up.ClaimedAt, up.ExpiresAt, daysLeft,
            available && up.ExpiresAt.HasValue && up.ExpiresAt.Value - nowUtc <= PromoRules.ExpiringSoonWindow,
            up.UsedAt, providerName, usage?.DiscountJod, usage?.BonusPoints);
    }
}
