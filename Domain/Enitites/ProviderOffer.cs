using Domain.Common;

namespace Domain.Enitites;

public class ProviderOffer : BaseAuditableEntity
{
    public string Title { get; set; } = null!;
    public string? TitleAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public int ProposedByUserId { get; set; }
    public int ApprovalStatus { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? ApprovalNote { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int PointsCost { get; set; }
    public decimal MonetaryValue { get; set; }
    public string CurrencyCode { get; set; } = "KWD";
    public int? MaxUsesPerUser { get; set; }
    public int? MaxTotalUses { get; set; }
    public int CurrentTotalUses { get; set; }
    public int OfferCodeExpiryMinutes { get; set; } = 30;
    public string? ImageUrl { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }

    public virtual UserAccount ProposedByUser { get; set; } = null!;
    public virtual UserAccount? ApprovedByUser { get; set; }
    public virtual ICollection<OfferTransaction> Transactions { get; set; } = new List<OfferTransaction>();
}
