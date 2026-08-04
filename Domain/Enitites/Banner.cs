using System.Security.AccessControl;
using Domain.Common;

namespace Domain.Enitites;

public partial class Banner : BaseAuditableEntity
{

    public string Name { get; set; } = null!;

    public string Phone { get; set; } = null!;
    public string Email { get; set; } = null!;
    public int? ActionType { get; set; }
    public string? ActionUrl { get; set; }

    // Location targeting: "national" (default) | "city" | "radius"
    public string TargetType { get; set; } = "national";
    public string? TargetCity { get; set; }
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    /// <summary>Radius for targetType = radius; null = use the admin global nearby radius.</summary>
    public double? RadiusKm { get; set; }

    // Optional link to a real entity (lets a banner BE a station/shop)
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }

    /// <summary>Optional paid top-spot weight, applied within a ranking layer.</summary>
    public int? Priority { get; set; }

    /// <summary>Billing link (ad campaign).</summary>
    public int? CampaignId { get; set; }
    public virtual Campaign? Campaign { get; set; }

    public ICollection<BannerDuration> BannerDurations { get; set; } = (List<BannerDuration>) [];
    public ICollection<BannerAttachment> BannerAttachments { get; set; } = (List<BannerAttachment>) [];

}