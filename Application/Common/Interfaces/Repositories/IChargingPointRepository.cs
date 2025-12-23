using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetChargingPointById;

namespace Application.Common.Interfaces.Repositories;

public interface IChargingPointRepository
{
    Task<List<GetAllChargingPointsDto>> GetAllChargingPoints(int? chargerPointTypeId ,string? cityName,CancellationToken cancellationToken);
    Task<GetChargingPointByIdDto>GetChargingPointById( int id, CancellationToken cancellationToken);
}