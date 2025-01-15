using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetAllChargingPointsByUser;

public record GetAllChargingPointsByUserRequest( int? ChargerPointTypeId, string? CityName, int? PlugTypeId)
    : IRequest<IEnumerable<GetAllChargingPointsDto>>;

public class GetAllChargingPointsByUserRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllChargingPointsByUserRequest, IEnumerable<GetAllChargingPointsDto>>
{
    public async Task<IEnumerable<GetAllChargingPointsDto>> Handle(GetAllChargingPointsByUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(x=>x.Id== currentUserService.UserId,cancellationToken);

        var query = from chargingPoint in applicationDbContext.ChargingPoints.AsNoTracking()
            join chargingPlug in applicationDbContext.ChargingPlugs.AsNoTracking() on chargingPoint.Id equals
                chargingPlug.ChargingPointId into chargingPlugGroup
            from chargingPlug in chargingPlugGroup.DefaultIfEmpty()
            join plugType in applicationDbContext.PlugTypes.AsNoTracking() on chargingPlug.PlugTypeId equals
                plugType.Id into plugTypeGroup
            from plugType in plugTypeGroup.DefaultIfEmpty()
            where !chargingPoint.IsDeleted && chargingPoint.OwnerId == user.Id
            select new GetAllChargingPointsDto(
                chargingPoint.Id,
                chargingPoint.Name,
                chargingPoint.CityName,
                chargingPoint.Phone,
                chargingPoint.FromTime,
                chargingPoint.ToTime,
                chargingPoint.Latitude,
                chargingPoint.Longitude,
                chargingPoint.StatusId,
                chargingPoint.ChargerPointTypeId,
                new PlugTypeSummary(plugType.Id, plugType.Name, plugType.SerialNumber)
            );

        if (!string.IsNullOrEmpty(request.CityName))
            query = query.Where(x => x.CityName.Contains(request.CityName));
        if (request.PlugTypeId.HasValue)
            query = query.Where(x => x.PlugType.Id == request.PlugTypeId);
        if (request.ChargerPointTypeId.HasValue)
            query = query.Where(x => x.ChargerPointTypeId == request.ChargerPointTypeId);

        var result = await query.ToListAsync(cancellationToken);

        return result;
    }
}