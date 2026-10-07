using Application.ChargingPoints.Queries.GetChargingPointById;
using Cable.Core.Utilities;

namespace Application.ChargingPoints.Queries;

public record GetAllChargingPointsDto(
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
    List<PlugTypeSummary> PlugTypeSummary,
    bool IsFavorite,
    bool IsPartner,
    bool HasOwner = false,
    DateTime? CreatedAt = null,
    DateTime? ModifiedAt = null,
    int? FavoritesCount = null,
    string? ViewImage = null,
    string? ViewImageStatus = null
)
{
    // Returned in dial-ready E.164 (+962...) so tel: links work without a
    // mobile release. Storage is unchanged (962... in the DB).
    public string? Phone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(Phone);
    public string? OwnerPhone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(OwnerPhone);
}
