namespace Application.ChargingPoints.Queries.GetChargingPointById;

public record GetChargingPointByIdDto(
    int Id,
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
    int VisitorsCount,
    ChargingPointTypeSummary? ChargerPointType,
    StatusSummary? Status,
    UserAccountSummary? Owner,
    List<PlugTypeSummary>? PlugType
)
{
    public double ChargingPointAverage { get; set; }
};



public record ChargingPointTypeSummary(int Id, string Name);

public record StatusSummary(int Id, string Name);

public record UserAccountSummary(int Id, string Name);

public record PlugTypeSummary(int Id, string? Name, string SerialNumber);

