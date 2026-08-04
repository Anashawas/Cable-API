using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetAllChargingPoints;

/// <summary>
/// Legacy stations list. The extra optional fields below are dispatch hints for
/// the route: when any is present the route sends GetChargingPointsPagedRequest
/// instead (A2b) — this handler ignores them.
/// </summary>
public record GetAllChargingPointsRequest(
    int? ChargerPointTypeId,
    string? CityName,
    string? Search = null,
    int? StatusId = null,
    int? ChargerBrandId = null,
    bool? IsVerified = null,
    int? PlugTypeId = null,
    string? Sort = null,
    int? Page = null,
    int? PageSize = null
) : IRequest<List<GetAllChargingPointsDto>>;

public class GetAllChargingPointsRequestHandler(
    IApplicationDbContext applicationDbContext,
    IChargingPointRepository chargingPointRepository,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService
    )
    : IRequestHandler<GetAllChargingPointsRequest, List<GetAllChargingPointsDto>>
{
    public async Task<List<GetAllChargingPointsDto>> Handle(GetAllChargingPointsRequest request,
        CancellationToken cancellationToken)
=> await chargingPointRepository.GetAllChargingPoints(request.ChargerPointTypeId, request.CityName,
    currentUserService.UserId, cancellationToken);
}