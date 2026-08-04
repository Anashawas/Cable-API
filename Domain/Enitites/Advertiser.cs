using Domain.Common;

namespace Domain.Enitites;

/// <summary>An ad customer (buys banners / premium slots / welcome messages).</summary>
public class Advertiser : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public string? Contact { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();
}
