using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarModelSizes.Queries.GetAllCarModelSizes;

public record GetAllCarModelSizesRequest() : IRequest<List<GetAllCarModelSizesDto>>;

public record GetAllCarModelSizesDto(int Id, string Name);

public class GetAllCarModelSizesRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllCarModelSizesRequest, List<GetAllCarModelSizesDto>>
{
    public async Task<List<GetAllCarModelSizesDto>> Handle(GetAllCarModelSizesRequest request,
        CancellationToken cancellationToken)
        => await applicationDbContext.CarModelSizes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new GetAllCarModelSizesDto(x.Id, x.Name))
            .ToListAsync(cancellationToken);
}
