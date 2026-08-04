using Application.Common.Models;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetAllChargingPointsByUser;

public record GetAllChargingPointsByUserRequest( int? ChargerPointTypeId, string? CityName, int? PlugTypeId, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<GetAllChargingPointsDto>>;

public class GetAllChargingPointsByUserRequestHandler(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllChargingPointsByUserRequest, PagedResult<GetAllChargingPointsDto>>
{
    public async Task<PagedResult<GetAllChargingPointsDto>> Handle(GetAllChargingPointsByUserRequest request,
        CancellationToken cancellationToken)
   =>    (await chargingPointRepository.GetAllChargingPoints(request.ChargerPointTypeId, request.CityName, currentUserService.UserId, cancellationToken))
            .ToOptionallyPaginated(request.Page, request.PageSize);
}