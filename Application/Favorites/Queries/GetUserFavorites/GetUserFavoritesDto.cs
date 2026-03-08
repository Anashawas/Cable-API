using Application.ChargingPoints.Queries.GetChargingPointById;

namespace Application.Favorites.Queries.GetUserFavorites;

public record GetUserFavoritesDto(
    int FavoriteId,
    int ChargingPointId,
    string Name,
    string? Address,
    string? CityName,
    string? CountryName,
    double Latitude,
    double Longitude,
    string? Phone,
    double? Price,
    string? FromTime,
    string? ToTime,
    bool IsVerified,
    int VisitorsCount,
    string? Icon,
    bool HasOffer,
    string? OfferDescription,
    double AvgRating,
    int RateCount,
    StatusSummary? Status,
    ChargingPointTypeSummary? ChargingPointType,
    List<PlugTypeSummary> PlugTypes,
    List<string> Images,
    DateTime? AddedToFavoritesAt
);
