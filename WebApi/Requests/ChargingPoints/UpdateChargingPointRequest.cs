namespace Cable.Requests.ChargingPoints;

public record UpdateChargingPointRequest(
    string Name,
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
    double Latitude,
    double Longitude,
    int ChargerPointTypeId,
    int StatusId,
    int StationTypeId,
    string?OwnerPhone,
    bool IsVerified,
    bool HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<int>? PlugTypeIds
    );

