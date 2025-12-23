using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Queries.GetAllUserCars;

public record GetAllUserCarsRequest : IRequest<List<GetAllUserCarsDto>>;

public record GetAllUserCarsDto(int CarTypeId, string CarTypeName, List<CarModelWithPlugs> CarModels);

public record CarModelWithPlugs(int CarModelId, string CarModelName, List<PlugTypeSummary> PlugTypes);

public record PlugTypeSummary(int Id, string? Name, string SerialNumber);

public class GetAllUserCarsQueryHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetAllUserCarsRequest, List<GetAllUserCarsDto>>
{
    public async Task<List<GetAllUserCarsDto>> Handle(GetAllUserCarsRequest request,
        CancellationToken cancellationToken)
    {
        var userCars = await applicationDbContext.UserCars

                .Include(x => x.CarModel)
                .ThenInclude(x => x.CarType)
            .Include(x => x.PlugType)
            .Where(x => x.UserId == currentUserService.UserId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var groupedByCarType = userCars
            .GroupBy(x => new { x.CarModel.CarType.Id, x.CarModel.CarType.Name })
            .Select(carTypeGroup => new GetAllUserCarsDto(
                carTypeGroup.Key.Id,
                carTypeGroup.Key.Name,
                carTypeGroup
                    .GroupBy(x => new { x.CarModel.Id, x.CarModel.Name })
                    .Select(carModelGroup => new CarModelWithPlugs(
                        carModelGroup.Key.Id,
                        carModelGroup.Key.Name,
                        carModelGroup.Select(x => new PlugTypeSummary(
                            x.PlugType.Id,
                            x.PlugType.Name,
                            x.PlugType.SerialNumber))
                        .ToList()))
                    .ToList()))
            .ToList();

        return groupedByCarType;
    }
}