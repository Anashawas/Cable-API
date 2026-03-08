using System;
using System.Collections.Generic;
using Domain.Common;

namespace Domain.Enitites;

public partial class ChargingPoint : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public int OwnerId { get; set; }
    public string? Note { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public string? Phone { get; set; }
    public string? MethodPayment { get; set; }
    public double? Price { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }

    public int? ChargerSpeed { get; set; }

    public int? ChargersCount { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int VisitorsCount { get; set; }
    public int ChargerPointTypeId { get; set; }
    public int StatusId { get; set; }

    public string? OwnerPhone { get; set; }
    public bool IsVerified { get; set; } 
    public bool HasOffer { get; set; } 
    public string? Service { get; set; }
    public string? OfferDescription { get; set; }
    public string? Address { get; set; }
    public string? Icon { get; set; }
    public int StationTypeId { get; set; }

    public string? ChargerBrand { get; set; }

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

    public virtual ICollection<ChargingPointAttachment> ChargingPointAttachments { get; set; } =
        new List<ChargingPointAttachment>();

    public virtual ChargingPointType ChargerPointType { get; set; } = null!;

    public virtual ICollection<ChargingPlug> ChargingPlugs { get; set; } = new List<ChargingPlug>();

    public virtual UserAccount Owner { get; set; } = null!;

    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    public virtual Status Status { get; set; } = null!;
    public virtual StationType StationType { get; set; } = null!;

    public virtual ICollection<UserComplaint> UserComplaints { get; set; } = new List<UserComplaint>();
    public virtual ICollection<UserFavoriteChargingPoint> UserFavorites { get; set; } = new List<UserFavoriteChargingPoint>();

}