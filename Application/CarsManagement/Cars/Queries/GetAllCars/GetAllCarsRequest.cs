using Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.Cars.Queries.GetAllCars;

public record GetAllCarsRequest : IRequest<List<GetAllCarsDto>>;

public record CarModelSummary(int Id, string Name, CarTypeSummary CarType);

public record GetAllCarsDto(CarModelSummary CarModel);

public class GetAllCarsQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllCarsRequest, List<GetAllCarsDto>>
{
    public async Task<List<GetAllCarsDto>> Handle(GetAllCarsRequest request, CancellationToken cancellationToken)
        => await applicationDbContext.Cars.Include(x => x.CarModel)
            .ThenInclude(x => x.CarType)
            .AsNoTracking().Select(x => new GetAllCarsDto(new CarModelSummary(x.CarModel.Id, x.CarModel.Name,
                new CarTypeSummary(x.CarModel.CarType.Id, x.CarModel.CarType.Name))))
            .ToListAsync(cancellationToken);
}