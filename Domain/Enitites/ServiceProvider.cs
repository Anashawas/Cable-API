using Domain.Common;

namespace Domain.Enitites;

public class ServiceProvider : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public int OwnerId { get; set; }
    public int ServiceCategoryId { get; set; }
    public int StatusId { get; set; }
    public string? Description { get; set; }
    public string? Phone { get; set; }
    public string? OwnerPhone { get; set; }
    public string? Address { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Price { get; set; }
    public string? PriceDescription { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public string? MethodPayment { get; set; }
    public int VisitorsCount { get; set; }
    public bool IsVerified { get; set; }
    public bool HasOffer { get; set; }
    public string? OfferDescription { get; set; }
    public string? Service { get; set; }
    public string? Icon { get; set; }
    public string? Note { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? WebsiteUrl { get; set; }

    // Loyalty Blocking
    public bool IsLoyaltyBlocked { get; set; }
    public DateTime? LoyaltyBlockedAt { get; set; }
    public DateTime? LoyaltyBlockedUntil { get; set; }
    public string? LoyaltyBlockReason { get; set; }
    public int? LoyaltyBlockedByUserId { get; set; }
    public virtual UserAccount? LoyaltyBlockedByUser { get; set; }

    // Loyalty Credit Limit
    public decimal? LoyaltyCreditLimit { get; set; }
    public decimal LoyaltyCurrentBalance { get; set; }

    public virtual UserAccount Owner { get; set; } = null!;
    public virtual ServiceCategory ServiceCategory { get; set; } = null!;
    public virtual Status Status { get; set; } = null!;
    public virtual ICollection<ServiceProviderAttachment> ServiceProviderAttachments { get; set; } = new List<ServiceProviderAttachment>();
    public virtual ICollection<ServiceProviderRate> ServiceProviderRates { get; set; } = new List<ServiceProviderRate>();
    public virtual ICollection<UserFavoriteServiceProvider> UserFavorites { get; set; } = new List<UserFavoriteServiceProvider>();
}
