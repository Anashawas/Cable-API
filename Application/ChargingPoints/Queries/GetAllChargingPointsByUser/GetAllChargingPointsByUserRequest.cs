using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetAllChargingPointsByUser;

public record GetAllChargingPointsByUserRequest( int? ChargerPointTypeId, string? CityName, int? PlugTypeId)
    : IRequest<IEnumerable<GetAllChargingPointsDto>>;

public class GetAllChargingPointsByUserRequestHandler(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllChargingPointsByUserRequest, IEnumerable<GetAllChargingPointsDto>>
{
    public async Task<IEnumerable<GetAllChargingPointsDto>> Handle(GetAllChargingPointsByUserRequest request,
        CancellationToken cancellationToken)
   =>    await chargingPointRepository.GetAllChargingPoints(request.ChargerPointTypeId, request.CityName, currentUserService.UserId, cancellationToken);
}