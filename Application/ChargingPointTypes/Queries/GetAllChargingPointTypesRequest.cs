using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPointTypes.Queries;

public record GetAllChargingPointTypesRequest : IRequest<List<GetAllChargingPointTypesDto>>;

public class GetChargingPointTypesQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllChargingPointTypesRequest, List<GetAllChargingPointTypesDto>>
{
    public async Task<List<GetAllChargingPointTypesDto>> Handle(GetAllChargingPointTypesRequest request,
        CancellationToken cancellationToken)
        => mapper.Map<List<GetAllChargingPointTypesDto>>(await applicationDbContext.ChargingPointTypes.AsNoTracking()
                .ToListAsync(cancellationToken));
}