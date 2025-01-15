namespace Application.ChargingPoints.Queries;

public record GetAllChargingPointsDto(
    int Id,
    string Name,
    string? CityName,
    string? Phone,
    string? FromTime,
    string? ToTime,
    double Latitude,
    double Longitude,
    int StatusId,
    int ChargerPointTypeId,
    PlugTypeSummary PlugType
);

public record PlugTypeSummary(int Id, string? Name, string SerialNumber);
