using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// The national time-of-use charging tariff (fils / kWh by hour of day) — the backend's
/// source of truth so a regulator change never needs an app update. One active row;
/// <see cref="Version"/> increases on every edit so the app knows to reschedule its
/// local fallback alerts.
/// </summary>
public class TouTariff : BaseAuditableEntity
{
    public int Version { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public string Timezone { get; set; } = "Asia/Amman";
    public string Currency { get; set; } = "JOD";
    public string Unit { get; set; } = "fils/kWh";
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }

    public virtual ICollection<TouTariffWindow> Windows { get; set; } = new List<TouTariffWindow>();
}

/// <summary>
/// One tariff window. Minutes from midnight (Asia/Amman); <see cref="EndMin"/> exceeds 1440
/// when the window crosses midnight (23:00 → 05:00 = 1380 → 1740), exactly like the app's
/// own calculation. <see cref="Key"/> is what user preferences are stored against — never
/// renamed.
/// </summary>
public class TouTariffWindow : BaseEntity
{
    public int TouTariffId { get; set; }
    public string Key { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public int StartMin { get; set; }
    public int EndMin { get; set; }

    /// <summary>offPeak | partial | peak</summary>
    public string Tier { get; set; } = null!;
    public int PriceFils { get; set; }
    public int SortOrder { get; set; }

    public virtual TouTariff Tariff { get; set; } = null!;
}

/// <summary>A user's price-alert preferences. Absent row = never asked for alerts (enabled false).</summary>
public class UserPriceAlert
{
    public int UserAccountId { get; set; }
    public bool IsEnabled { get; set; }

    /// <summary>15 | 30 | 45 | 60</summary>
    public int LeadMinutes { get; set; } = 15;

    /// <summary>CSV of window keys, e.g. "offPeak,peak".</summary>
    public string Windows { get; set; } = "";
    public DateTime UpdatedAt { get; set; }

    public virtual UserAccount UserAccount { get; set; } = null!;
}

/// <summary>One alert actually sent (or skipped with a reason) — the unique index is what stops a late or re-run job from sending twice.</summary>
public class PriceAlertLog
{
    public long Id { get; set; }
    public int UserAccountId { get; set; }
    public string WindowKey { get; set; } = null!;

    /// <summary>The Jordan date of the window start the alert was for.</summary>
    public DateOnly AlertDate { get; set; }
    public DateTime SentAt { get; set; }
    public int LeadMinutes { get; set; }
    public string? Language { get; set; }
}
