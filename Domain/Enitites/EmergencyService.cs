using Domain.Common;

namespace Domain.Enitites;

public class EmergencyService : BaseAuditableEntity
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int SubscriptionType { get; set; } // 0 = Normal, 1 = Premium
    public string? PriceDetails { get; set; }
    public string? ActionUrl { get; set; }
    public TimeSpan? OpenFrom { get; set; }
    public TimeSpan? OpenTo { get; set; }
    public string? PhoneNumber { get; set; }
    public string? WhatsAppNumber { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    public virtual ICollection<EmergencyServiceAttachment> EmergencyServiceAttachments { get; set; } = new List<EmergencyServiceAttachment>();
}
