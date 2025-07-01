using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetAllChargingPoints;

public record GetAllChargingPointsRequest(
    int? ChargerPointTypeId, string? CityName
    ) : IRequest<List<GetAllChargingPointsDto>>;

public class GetAllChargingPointsRequestHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllChargingPointsRequest, List<GetAllChargingPointsDto>>
{
    public async Task<List<GetAllChargingPointsDto>> Handle(GetAllChargingPointsRequest request,
        CancellationToken cancellationToken)
    {
        var query = from chargingPoint in applicationDbContext.ChargingPoints.AsNoTracking()
            join chargingPlug in applicationDbContext.ChargingPlugs.AsNoTracking() on chargingPoint.Id equals
                chargingPlug.ChargingPointId into chargingPlugGroup
            from chargingPlug in chargingPlugGroup.DefaultIfEmpty()
            where !chargingPoint.IsDeleted 
            select new GetAllChargingPointsDto(
                chargingPoint.Id,
                chargingPoint.Name,
                chargingPoint.CityName,
                chargingPoint.Phone,
                chargingPoint.FromTime,
                chargingPoint.ToTime,
                chargingPoint.Latitude,
                chargingPoint.Longitude,
                chargingPoint.ChargerPointTypeId
               
            );

        if (!string.IsNullOrEmpty(request.CityName))
            query = query.Where(x => x.CityName != null && x.CityName.Contains(request.CityName));
        if (request.ChargerPointTypeId.HasValue)
            query = query.Where(x => x.ChargerPointTypeId == request.ChargerPointTypeId);

        var result = await query.ToListAsync(cancellationToken);

        return result;
    }
}