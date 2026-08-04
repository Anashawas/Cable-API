using Microsoft.EntityFrameworkCore;

namespace Application.ChargerBrands.Queries.GetAllChargerBrands;

public record GetAllChargerBrandsRequest() : IRequest<List<GetAllChargerBrandsDto>>;

public record GetAllChargerBrandsDto(int Id, string Name);

public class GetAllChargerBrandsRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllChargerBrandsRequest, List<GetAllChargerBrandsDto>>
{
    public async Task<List<GetAllChargerBrandsDto>> Handle(GetAllChargerBrandsRequest request,
        CancellationToken cancellationToken)
        => await applicationDbContext.ChargerBrands
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new GetAllChargerBrandsDto(x.Id, x.Name))
            .ToListAsync(cancellationToken);
}
