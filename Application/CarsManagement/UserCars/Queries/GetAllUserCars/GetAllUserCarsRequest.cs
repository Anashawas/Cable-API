using Application.CarsManagement.Cars.Queries.GetAllCars;
using Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Queries.GetAllUserUserCars;

public record GetAllUserCarsRequest : IRequest<List<GetAllUserCarsDto>>;

public record CarSummary(CarModelSummary CarModel);

public record GetAllUserCarsDto(UserAccountSummary UserAccount, CarSummary Car);

public class GetAllUserCarsQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllUserCarsRequest, List<GetAllUserCarsDto>>
{
    public async Task<List<GetAllUserCarsDto>> Handle(GetAllUserCarsRequest request,
        CancellationToken cancellationToken)
        => await applicationDbContext.UserCars.Include(x => x.UserAccount)
            .Include(x => x.Car)
            .AsNoTracking()
            .Select(x => new GetAllUserCarsDto(new UserAccountSummary(x.UserAccount.Id, x.UserAccount.Name),
                new CarSummary(new CarModelSummary(x.Car.CarModel.Id, x.Car.CarModel.Name,
                    new CarTypeSummary(x.Car.CarModel.CarType.Id, x.Car.CarModel.CarType.Name)))))
            .ToListAsync(cancellationToken);
}