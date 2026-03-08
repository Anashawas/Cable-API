using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Favorites.Queries.GetUserFavorites;

namespace Application.Common.Interfaces.Repositories;

public interface IChargingPointRepository
{
    Task<List<GetAllChargingPointsDto>> GetAllChargingPoints(int? chargerPointTypeId, string? cityName, int? userId, CancellationToken cancellationToken);
    Task<GetChargingPointByIdDto> GetChargingPointById(int id, int? userId, CancellationToken cancellationToken);
    Task<List<GetUserFavoritesDto>> GetUserFavoriteChargingPoints(int userId, CancellationToken cancellationToken);
    Task<List<GetAllChargingPointsDto>> GetChargingPointsByOwner(int ownerId, int? chargerPointTypeId, string? cityName, CancellationToken cancellationToken);
}