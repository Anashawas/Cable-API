using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A sold, targeted home-screen takeover ("welcome message"). Fully dynamic:
/// bilingual content, one action button, location/audience targeting, schedule
/// window and per-user frequency caps — all admin-managed, no app release.
/// </summary>
public class Announcement : BaseAuditableEntity
{
    public string TitleEn { get; set; } = null!;
    public string TitleAr { get; set; } = null!;
    public string BodyEn { get; set; } = null!;
    public string BodyAr { get; set; } = null!;
    public string? ImageUrl { get; set; }

    /// <summary>Same 1..5 contract as banners (URL / WhatsApp / charger / maps / contact-us).</summary>
    public int? ActionType { get; set; }
    public string? ActionUrl { get; set; }

    /// <summary>Optional custom CTA button text (null → app default).</summary>
    public string? ActionLabelEn { get; set; }
    public string? ActionLabelAr { get; set; }

    // Targeting: "national" | "city" | "radius"
    public string TargetType { get; set; } = "national";
    public string? TargetCity { get; set; }
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    public double? RadiusKm { get; set; }

    /// <summary>"all" | "guests" | "loggedIn"</summary>
    public string Audience { get; set; } = "all";

    // Scheduling + frequency caps
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int MaxPerDay { get; set; } = 1;
    public int CooldownHours { get; set; } = 24;
    public int MaxLifetime { get; set; } = 3;
    public bool StopOnDismiss { get; set; } = true;

    public bool IsActive { get; set; } = true;

    // Billing
    public int? CampaignId { get; set; }
    public int? AdvertiserId { get; set; }
    public virtual Campaign? Campaign { get; set; }
    public virtual Advertiser? Advertiser { get; set; }
}
