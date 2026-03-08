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
    string? ChargerBrand,
    StatusSummary StatusSummary,
    ChargingPointTypeSummary? ChargingPointType,
    StationTypeSummary? StationType,
    List<string>? Images,
    List<PlugTypeSummary>? PlugTypeSummary,
    bool IsFavorite,
    bool IsPartner
);



public record ChargingPointTypeSummary(int Id, string Name);

public record StationTypeSummary(int Id, string Name);

public record StatusSummary(int Id, string Name);

public record UserAccountSummary(int Id, string? Name);

public record PlugTypeSummary(int? Id, string Name, string SerialNumber);

