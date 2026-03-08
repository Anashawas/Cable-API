using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetAllChargingPoints;

public record GetAllChargingPointsRequest(
    int? ChargerPointTypeId,
    string? CityName
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