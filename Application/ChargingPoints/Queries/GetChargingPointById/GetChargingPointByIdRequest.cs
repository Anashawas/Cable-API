using Application.Common.Interfaces.Repositories;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetChargingPointById;

public record GetChargingPointByIdRequest(int Id) : IRequest<GetChargingPointByIdDto>;

public class GetChargingPointByIdQuery(IApplicationDbContext applicationDbContext, IRateRepository rateRepository)
    : IRequestHandler<GetChargingPointByIdRequest, GetChargingPointByIdDto>
{
    public async Task<GetChargingPointByIdDto> Handle(GetChargingPointByIdRequest request,
        CancellationToken cancellationToken)
    {
        var result = await (from chargingPoint in applicationDbContext.ChargingPoints
                             join userAccount in applicationDbContext.UserAccounts on chargingPoint.OwnerId
                                 equals userAccount.Id
                             join chargerPointType in applicationDbContext.ChargingPointTypes on chargingPoint
                                     .ChargerPointTypeId
                                 equals chargerPointType.Id
                             join chargingPlug in applicationDbContext.ChargingPlugs on chargingPoint.Id equals
                                 chargingPlug.ChargingPointId into chargingPlugGroup
                             from chargingPlug in chargingPlugGroup.DefaultIfEmpty()
                             join plugType in applicationDbContext.PlugTypes on chargingPlug.PlugTypeId equals
                                 plugType.Id
                             join status in applicationDbContext.Statuses on chargingPoint.StatusId equals status.Id
                             where !chargingPoint.IsDeleted && chargingPoint.Id == request.Id
                             select new GetChargingPointByIdDto(
                                 chargingPoint.Id,
                                 chargingPoint.Name,
                                 chargingPoint.Note,
                                 chargingPoint.CountryName,
                                 chargingPoint.CityName,
                                 chargingPoint.Phone,
                                 chargingPoint.MethodPayment,
                                 chargingPoint.Price,
                                 chargingPoint.FromTime,
                                 chargingPoint.ToTime,
                                 chargingPoint.ChargerSpeed,
                                 chargingPoint.ChargersCount,
                                 chargingPoint.Latitude,
                                 chargingPoint.Longitude,
                                 chargingPoint.VisitorsCount,
                                 new ChargingPointTypeSummary(chargerPointType.Id, chargerPointType.Name),
                                 new StatusSummary(status.Id, status.Name),
                                 new UserAccountSummary(userAccount.Id, userAccount.Name),
                                 chargingPoint.ChargingPlugs.Select(x => new PlugTypeSummary(x.PlugType.Id,
                                     x.PlugType.Name,
                                     x.PlugType.SerialNumber)).ToList()
                             )
                         ).AsNoTracking().FirstOrDefaultAsync(cancellationToken) ??
                     throw new NotFoundException($"can not find charging point with id {request.Id}");

        result.ChargingPointAverage = await rateRepository.GetChargingPointRateAverage(request.Id, cancellationToken);
        return result;
    }
}