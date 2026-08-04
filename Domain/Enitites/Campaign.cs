using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A sold ad campaign: what an advertiser paid for, over which window, at what
/// price. Banners / premium stations / announcements link to it for billing
/// and reporting.
/// </summary>
public class Campaign : BaseAuditableEntity
{
    public int AdvertiserId { get; set; }

    /// <summary>"banner" | "premium" | "welcome"</summary>
    public string Type { get; set; } = null!;

    public string? CityArea { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal Price { get; set; }

    /// <summary>0 = Draft, 1 = Active, 2 = Paused, 3 = Ended.</summary>
    public int Status { get; set; }

    public virtual Advertiser Advertiser { get; set; } = null!;
}
