using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;

public record GetAllCarsModelsRequest() : IRequest<List<GetAllCarsModelsDto>>;

public record GetAllCarsModelsDto(string Name, CarTypeSummary CarType);

public record CarTypeSummary(int Id, string Name);

public class GetAllCarsModelsRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllCarsModelsRequest, List<GetAllCarsModelsDto>>
{
    public async Task<List<GetAllCarsModelsDto>> Handle(GetAllCarsModelsRequest request,
        CancellationToken cancellationToken)
        => await applicationDbContext.CarModels.Include(x => x.CarType).AsNoTracking()
            .Select(x => new GetAllCarsModelsDto(x.Name, new CarTypeSummary(x.CarType.Id, x.CarType.Name)))
            .ToListAsync(cancellationToken);
}