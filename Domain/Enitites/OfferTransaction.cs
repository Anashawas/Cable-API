using Domain.Common;

namespace Domain.Enitites;

public class OfferTransaction : BaseAuditableEntity
{
    public int ProviderOfferId { get; set; }
    public int? UserId { get; set; }
    public string OfferCode { get; set; } = null!;
    public int Status { get; set; }
    public int PointsDeducted { get; set; }
    public decimal MonetaryValue { get; set; }
    public string CurrencyCode { get; set; } = "KWD";
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public DateTime CodeExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public virtual ProviderOffer Offer { get; set; } = null!;
    public virtual UserAccount? User { get; set; }
    public virtual UserAccount? ConfirmedByUser { get; set; }
}
