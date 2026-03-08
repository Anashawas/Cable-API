using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;

namespace Application.ChargingPoints.Queries.GetMyChargingPoints;

public record GetMyChargingPointsRequest(int? ChargerPointTypeId, string? CityName)
    : IRequest<IEnumerable<GetAllChargingPointsDto>>;

public class GetMyChargingPointsRequestHandler(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyChargingPointsRequest, IEnumerable<GetAllChargingPointsDto>>
{
    public async Task<IEnumerable<GetAllChargingPointsDto>> Handle(GetMyChargingPointsRequest request,
        CancellationToken cancellationToken)
    {
        return await chargingPointRepository.GetChargingPointsByOwner(
            currentUserService.UserId!.Value,
            request.ChargerPointTypeId,
            request.CityName,
            cancellationToken);
    }
}
