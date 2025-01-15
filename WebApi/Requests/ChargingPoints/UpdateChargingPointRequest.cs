namespace Cable.Requests.ChargingPoints;

public record UpdateChargingPointRequest(string Name,
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
    int ChargerPointTypeId);
