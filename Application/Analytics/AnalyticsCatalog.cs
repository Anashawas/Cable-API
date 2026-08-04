using Cable.Core.Emuns;

namespace Application.Analytics;

/// <summary>
/// Single source of truth for analytics entity types and which event types are
/// valid for each. Keeps the (EntityType, EventType) contract in one place so
/// validators, handlers and the engine agree.
/// </summary>
public static class AnalyticsCatalog
{
    public const string ChargingPoint   = "ChargingPoint";
    public const string ServiceProvider = "ServiceProvider";
    public const string Banner          = "Banner";
    public const string Announcement    = "Announcement";

    private static readonly AnalyticsEventType[] ProviderEvents =
    {
        AnalyticsEventType.FullView,
        AnalyticsEventType.HalfView,
        AnalyticsEventType.CallButtonClick,
        AnalyticsEventType.MapClick
    };

    // Premium home-card events only apply to charging points (the paid home slot).
    private static readonly AnalyticsEventType[] ChargingPointEvents =
        ProviderEvents.Concat(new[]
        {
            AnalyticsEventType.PremiumView,
            AnalyticsEventType.PremiumClick
        }).ToArray();

    private static readonly AnalyticsEventType[] BannerEvents =
    {
        AnalyticsEventType.BannerView,
        AnalyticsEventType.BannerClick
    };

    private static readonly AnalyticsEventType[] AnnouncementEvents =
    {
        AnalyticsEventType.BannerView,     // 20 = welcome view
        AnalyticsEventType.BannerClick,    // 21 = welcome click
        AnalyticsEventType.Dismiss,        // 22
        AnalyticsEventType.CtaConversion   // 23
    };

    /// <summary>True for any entity type the engine understands.</summary>
    public static bool IsKnownEntityType(string entityType) =>
        entityType is ChargingPoint or ServiceProvider or Banner or Announcement;

    /// <summary>True for the two provider entity types (owned by a Provider user).</summary>
    public static bool IsProviderType(string entityType) =>
        entityType is ChargingPoint or ServiceProvider;

    /// <summary>True when the event type is allowed for the given entity type.</summary>
    public static bool IsValidCombination(string entityType, AnalyticsEventType eventType) =>
        entityType switch
        {
            ChargingPoint   => ChargingPointEvents.Contains(eventType),
            ServiceProvider => ProviderEvents.Contains(eventType),
            Banner          => BannerEvents.Contains(eventType),
            Announcement    => AnnouncementEvents.Contains(eventType),
            _               => false
        };
}
