using Cable.Core.Utilities;

namespace Application.ChargingPoints.Queries.GetChargingPointById;

public record GetChargingPointByIdDto(
    int Id,
    string Name,
    string? CityName,
    string? CountryName,
    string? Phone,
    string? OwnerPhone,
    string? FromTime,
    string? ToTime,
    double Latitude,
    double Longitude,
    bool IsVerified,
    bool HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    double? AvgChargingPointRate,
    string? IConUrl,
    int RateCount,
    double? Price,
    int? ChargerSpeed,
    int? ChargersCount,
    int? VisitorsCount,
    string? Note,
    string? MethodPayment,
    StatusSummary StatusSummary,
    ChargingPointTypeSummary? ChargingPointType,
    StationTypeSummary? StationType,
    List<string>? Images,
    List<PlugTypeSummary>? PlugTypeSummary,
    bool IsFavorite,
    bool IsPartner,
    DateTime? PremiumPaymentDate = null,
    DateTime? PremiumExpiresAt = null,
    int? OwnerId = null,
    string? OwnerName = null,
    string? OwnerEmail = null,
    string? OwnerAccountPhone = null,
    List<ChargerBrandSummary>? ChargerBrands = null,
    DateTime? CreatedAt = null,
    DateTime? ModifiedAt = null
)
{
    // Returned in dial-ready E.164 (+962...) so tel: links work without a
    // mobile release. Storage is unchanged (962... in the DB).
    public string? Phone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(Phone);
    public string? OwnerPhone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(OwnerPhone);
    public string? OwnerAccountPhone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(OwnerAccountPhone);
}

/// <summary>A charger brand at the station and how many chargers of it.</summary>
public record ChargerBrandSummary(int Id, string Name, int Count);



public record ChargingPointTypeSummary(int Id, string Name);

public record StationTypeSummary(int Id, string Name);

public record StatusSummary(int Id, string Name);

public record UserAccountSummary(int Id, string? Name);

public record PlugTypeSummary(int? Id, string Name, string SerialNumber);

