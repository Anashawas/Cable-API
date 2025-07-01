namespace Application.CarsManagement.Cars.Commands.AddCar;

public record AddCarCommand(int CarModelId) : IRequest<int>;

public class AddCarCommandHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<AddCarCommand, int>
{
    public async Task<int> Handle(AddCarCommand request, CancellationToken cancellationToken)
    {
        var car = new Car()
        {
            CarModelId = request.CarModelId,
        };
        applicationDbContext.Cars.Add(car);
        await applicationDbContext.SaveChanges(cancellationToken);
        return car.CarModelId;
    }
}