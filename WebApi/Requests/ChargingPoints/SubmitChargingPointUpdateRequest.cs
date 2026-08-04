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
    int? StatusId,
    string? OwnerPhone,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<int>? PlugTypeIds,
    List<int>? AttachmentsToDelete
);
