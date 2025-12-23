using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;

public record GetAllCarsModelsRequest() : IRequest<List<GetAllCarsModelsDto>>;

public record GetAllCarsModelsDto(int Id, string Name, List<CarModelSummary> CarModels);

public record CarModelSummary(int Id, string Name);

public class GetAllCarsModelsRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllCarsModelsRequest, List<GetAllCarsModelsDto>>
{
    public async Task<List<GetAllCarsModelsDto>> Handle(GetAllCarsModelsRequest request,
        CancellationToken cancellationToken)
    {
        var carTypes = await applicationDbContext.CarTypes
            .Include(x => x.CarModels)
            .AsNoTracking()
            .Select(x => new GetAllCarsModelsDto(
                x.Id,
                x.Name,
                x.CarModels.Select(m => new CarModelSummary(m.Id, m.Name)).ToList()
            ))
            .ToListAsync(cancellationToken);

        return carTypes;
    }
}