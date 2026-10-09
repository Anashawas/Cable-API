using System;
using System.Collections.Generic;
using Domain.Common;

namespace Domain.Enitites;

public partial class ChargingPoint : BaseAuditableEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Owner user account; null = unassigned station.</summary>
    public int? OwnerId { get; set; }
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

    /// <summary>
    /// Demo/QA station (e.g. for App Store review). Excluded from public discovery
    /// lists, but still visible to its owner and when fetched by Id.
    /// </summary>
    public bool IsTest { get; set; }

    /// <summary>Paid home-card promo image (one per station, partner-uploaded, admin-reviewed).</summary>
    public string? ViewImage { get; set; }

    /// <summary>"pending" | "approved" | "rejected"; null when no image. Only approved is served to B2C.</summary>
    public string? ViewImageStatus { get; set; }

    /// <summary>Charger brands at this station with per-brand charger counts (source of truth).</summary>
    public virtual ICollection<ChargingPointChargerBrand> ChargerBrands { get; set; } =
        new List<ChargingPointChargerBrand>();

    // Loyalty Blocking
    public bool IsLoyaltyBlocked { get; set; }
    public DateTime? LoyaltyBlockedAt { get; set; }
    public DateTime? LoyaltyBlockedUntil { get; set; }
    public string? LoyaltyBlockReason { get; set; }
    public int? LoyaltyBlockedByUserId { get; set; }
    public virtual UserAccount? LoyaltyBlockedByUser { get; set; }

    // Wallet system (unified balance + credit limit)
    public decimal? WalletCreditLimit { get; set; }
    public decimal WalletBalance { get; set; }

    // Premium subscription (latest values, synced from Subscription/Payment; history lives there)
    public DateTime? PremiumPaymentDate { get; set; }
    public DateTime? PremiumExpiresAt { get; set; }

    /// <summary>Part B F4: when on, a worker's fan announcement sends directly (no owner approval).</summary>
    public bool AutoApproveWorkerNotifications { get; set; }

    // Cable Connect — live charger data visible to drivers only when ALL gates are open:
    // active OcppConnect subscription (admin) · ShareLiveStatus (the owner's consent) ·
    // !LiveStatusBlocked (admin veto: may switch OFF what the owner switched on, never the
    // reverse) · fresh data (per charger). ShareLiveStatus turns on with the first
    // subscription activation unless the owner already decided (ShareLiveStatusSetAt).
    public bool ShareLiveStatus { get; set; }
    public DateTime? ShareLiveStatusSetAt { get; set; }
    public int? ShareLiveStatusSetByUserId { get; set; }
    public bool LiveStatusBlocked { get; set; }
    public DateTime? LiveStatusBlockedAt { get; set; }
    public int? LiveStatusBlockedByUserId { get; set; }
    public string? LiveStatusBlockReason { get; set; }

    // Cable Connect — alert thresholds in minutes for this station; null = OcppLimits default.
    public int? OcppOfflineAlertMin { get; set; }
    public int? OcppFaultedAlertMin { get; set; }
    public int? OcppLongSessionAlertMin { get; set; }
    public int? OcppParkedAlertMin { get; set; }

    public virtual ICollection<ChargingPointAttachment> ChargingPointAttachments { get; set; } =
        new List<ChargingPointAttachment>();

    public virtual ChargingPointType ChargerPointType { get; set; } = null!;

    public virtual ICollection<ChargingPlug> ChargingPlugs { get; set; } = new List<ChargingPlug>();

    public virtual UserAccount? Owner { get; set; }

    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    public virtual Status Status { get; set; } = null!;
    public virtual StationType StationType { get; set; } = null!;

    public virtual ICollection<UserComplaint> UserComplaints { get; set; } = new List<UserComplaint>();
    public virtual ICollection<UserFavoriteChargingPoint> UserFavorites { get; set; } = new List<UserFavoriteChargingPoint>();

}