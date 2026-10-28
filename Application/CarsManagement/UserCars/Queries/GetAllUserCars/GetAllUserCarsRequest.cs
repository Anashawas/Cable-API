using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Queries.GetAllUserCars;

public record GetAllUserCarsRequest : IRequest<List<GetAllUserCarsDto>>;

public record GetAllUserCarsDto(int CarTypeId, string CarTypeName, List<CarModelWithPlugs> CarModels);

/// <param name="ReportsSoc">
/// Cable Connect learning: true once a car of this model reported its battery % during a session; null = not seen yet.
/// The app hides the "stop at battery %" option unless it is true (and always on AC chargers).
/// </param>
public record CarModelWithPlugs(int CarModelId, string CarModelName, List<PlugTypeSummary> PlugTypes, bool? ReportsSoc = null);

/// <param name="UserCarId">The user's car row — what POST /api/users/me/ocpp-sessions/start takes as carId.</param>
public record PlugTypeSummary(int Id, string? Name, string SerialNumber, int? UserCarId = null);

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
                            x.PlugType.SerialNumber,
                            x.Id))
                        .ToList(),
                        carModelGroup.First().CarModel.ReportsSoc))
                    .ToList()))
            .ToList();

        return groupedByCarType;
    }
}