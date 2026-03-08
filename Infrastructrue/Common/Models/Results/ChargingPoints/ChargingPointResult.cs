namespace Infrastructrue.Common.Models.Results.ChargingPoints;

internal class ChargingPointResult
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? OwnerPhone { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AvgChargingPointRate { get; set; }
    public int RateCount { get; set; }
    public int StatusId { get; set; }
    public string StatusName { get; set; } = null!;
    public int? PlugTypeId { get; set; }
    public string? PlugTypeName { get; set; }
    public string? SerialNumber { get; set; }
    public int ChargingPointTypeId { get; set; }
    public string ChargingPointTypeName { get; set; } = null!;
    public int StationTypeId { get; set; }
    public string StationTypeName { get; set; } = null!;
    public string? CityName { get; set; } = null!;
    public string? CountryName { get; set; }
    public bool IsVerified { get; set; }
    public double? Price { get; set; }
    public int? ChargerSpeed { get; set; }
    public int? ChargersCount { get; set; }
    public int? VisitorsCount { get; set; }
    public bool HasOffer { get; set; }
    public string? Service { get; set; }
    public string? OfferDescription { get; set; }
    public string? Note { get; set; }
    public string? Icon { get; set; }
    public string? FileName { get; set; }
    public string? MethodPayment { get; set; }
    public string? ChargerBrand { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsPartner { get; set; }
}
internal class
    ChargingPointsResult
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? OwnerPhone { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AvgChargingPointRate { get; set; }
    public int RateCount { get; set; }
    public int StatusId { get; set; }
    public string StatusName { get; set; } = null!;
    public int? PlugTypeId { get; set; }
    public string? PlugTypeName { get; set; }
    public string? SerialNumber { get; set; }
    public int ChargingPointTypeId { get; set; }
    public string? ChargingPointTypeName { get; set; }
    public int StationTypeId { get; set; }
    public string? StationTypeName { get; set; }
    public string? CityName { get; set; } = null!;
    public string? CountryName { get; set; }
    public bool IsVerified { get; set; }
    public double? Price { get; set; }
    public int? ChargerSpeed { get; set; }
    public int? ChargersCount { get; set; }
    public int? VisitorsCount { get; set; }
    public bool HasOffer { get; set; }
    public string? Service { get; set; }
    public string? OfferDescription { get; set; }
    public string? Note { get; set; }
    public string? Icon { get; set; }
    public string? FileName { get; set; }
    public string? MethodPayment { get; set; }
    public string? ChargerBrand { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsPartner { get; set; }
}

internal class UserFavoriteChargingPointResult
{
    public int FavoriteId { get; set; }
    public DateTime? AddedToFavoritesAt { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? OwnerPhone { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AvgChargingPointRate { get; set; }
    public int RateCount { get; set; }
    public int StatusId { get; set; }
    public string StatusName { get; set; } = null!;
    public int? PlugTypeId { get; set; }
    public string? PlugTypeName { get; set; }
    public string? SerialNumber { get; set; }
    public int ChargingPointTypeId { get; set; }
    public string? ChargingPointTypeName { get; set; }
    public int StationTypeId { get; set; }
    public string? StationTypeName { get; set; }
    public string? CityName { get; set; }
    public string? CountryName { get; set; }
    public bool IsVerified { get; set; }
    public double? Price { get; set; }
    public int? ChargerSpeed { get; set; }
    public int? ChargersCount { get; set; }
    public int? VisitorsCount { get; set; }
    public bool HasOffer { get; set; }
    public string? Service { get; set; }
    public string? OfferDescription { get; set; }
    public string? Note { get; set; }
    public string? Icon { get; set; }
    public string? FileName { get; set; }
    public string? MethodPayment { get; set; }
}
