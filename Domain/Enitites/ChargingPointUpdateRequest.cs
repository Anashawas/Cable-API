using Cable.Core.Enums;
using Domain.Common;

namespace Domain.Enitites;

public class ChargingPointUpdateRequest : BaseAuditableEntity
{
    public int ChargingPointId { get; set; }
    public int RequestedByUserId { get; set; }
    public RequestStatus RequestStatus { get; set; }

    // Charging Point Fields (nullable - only populated if changed)
    public string? Name { get; set; }
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
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? ChargerPointTypeId { get; set; }
    public int? StationTypeId { get; set; }
    public string? OwnerPhone { get; set; }
    public bool? HasOffer { get; set; }
    public string? Service { get; set; }
    public string? OfferDescription { get; set; }
    public string? Address { get; set; }
    public string? ChargerBrand { get; set; }

    // Icon change tracking
    public string? NewIcon { get; set; }
    public string? OldIcon { get; set; }

    // Plug types (stored as JSON)
    public string? NewPlugTypeIds { get; set; } // JSON: "[1,2,3]"
    public string? OldPlugTypeIds { get; set; } // JSON: "[2,3]"

    // Review information
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }

    // Navigation properties
    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
    public virtual UserAccount RequestedBy { get; set; } = null!;
    public virtual UserAccount? ReviewedBy { get; set; }
    public virtual ICollection<ChargingPointUpdateRequestAttachment> AttachmentChanges { get; set; }
        = new List<ChargingPointUpdateRequestAttachment>();
}
