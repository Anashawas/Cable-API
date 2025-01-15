using Application.Common.Interfaces.Repositories;

namespace Application.Rates.Queries.GetChargingPointRateById;

public record GetChargingPointRateByIdRequest(int ChargingPointId) : IRequest<GetChargingPointRateById>;

public class GetChargingPointRateByIdQueryHandler(IRateRepository rateRepository)
    : IRequestHandler<GetChargingPointRateByIdRequest, GetChargingPointRateById>
{
    public async Task<GetChargingPointRateById> Handle(GetChargingPointRateByIdRequest request,
        CancellationToken cancellationToken)
        => new(await rateRepository.GetChargingPointRateAverage(request.ChargingPointId, cancellationToken));
}