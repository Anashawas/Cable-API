using Application.Common.Models;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;

namespace Application.ChargingPoints.Queries.GetMyChargingPoints;

public record GetMyChargingPointsRequest(int? ChargerPointTypeId, string? CityName, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<GetAllChargingPointsDto>>;

public class GetMyChargingPointsRequestHandler(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyChargingPointsRequest, PagedResult<GetAllChargingPointsDto>>
{
    public async Task<PagedResult<GetAllChargingPointsDto>> Handle(GetMyChargingPointsRequest request,
        CancellationToken cancellationToken)
    {
        var points = await chargingPointRepository.GetChargingPointsByOwner(
            currentUserService.UserId!.Value,
            request.ChargerPointTypeId,
            request.CityName,
            cancellationToken);

        return points.ToOptionallyPaginated(request.Page, request.PageSize);
    }
}
