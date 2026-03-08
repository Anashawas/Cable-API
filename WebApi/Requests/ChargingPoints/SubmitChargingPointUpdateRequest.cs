namespace Cable.Requests.ChargingPoints;

/// <summary>
/// Request to submit charging point update for admin approval
/// </summary>
public record SubmitChargingPointUpdateRequest(
    string? Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double? Latitude,
    double? Longitude,
    int? ChargerPointTypeId,
    int? StationTypeId,
    string? OwnerPhone,
    bool? HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    string? ChargerBrand,
    List<int>? PlugTypeIds,
    List<int>? AttachmentsToDelete
);
