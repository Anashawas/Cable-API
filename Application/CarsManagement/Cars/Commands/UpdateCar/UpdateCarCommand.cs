using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.Cars.Commands.UpdateCar;

public record UpdateCarCommand(int Id, int CarModelId) : IRequest;

public class UpdateCarCommandHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<UpdateCarCommand>
{
    public async Task Handle(UpdateCarCommand request, CancellationToken cancellationToken)
    {
        var car = await applicationDbContext.Cars.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken) ??
                  throw new Exception("Car not found");
        car.CarModelId = request.CarModelId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}