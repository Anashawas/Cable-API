using Domain.Common;

namespace Domain.Enitites;

public partial class BannerDuration : BaseAuditableEntity
{
    public int BannerId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Set when this run was bought through a Subscription; null for manually scheduled runs.</summary>
    public int? SubscriptionId { get; set; }

    public Banner Banner { get; set; } = null!;
    
}