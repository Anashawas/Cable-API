namespace Cable.Core.Emuns;

/// <summary>
/// Every kind of analytics interaction the engine can record.
/// Grouped in ranges so new event types can be added without renumbering:
///   1–19  provider engagement (ChargingPoint / ServiceProvider)
///   20–29 banner engagement
///   30–39 paid home-screen placements (premium card)
/// </summary>
public enum AnalyticsEventType
{
    // Provider engagement
    FullView        = 1,
    HalfView        = 2,
    CallButtonClick = 3,
    MapClick        = 4,

    // Banner / announcement engagement (20-21 shared: view / click)
    BannerView      = 20,
    BannerClick     = 21,
    Dismiss         = 22,
    CtaConversion   = 23,

    // Premium home-card engagement (ChargingPoint shown as a paid ad on home)
    PremiumView     = 30,
    PremiumClick    = 31
}
