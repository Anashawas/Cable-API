namespace Application.CarsManagement.UserCars.Commands.AddUserCar;

public record AddUserCarCommand(int UserId, int CarId, int PlugTypeId) : IRequest<int>;

public class AddUserCarCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddUserCarCommand, int>
{
    public async Task<int> Handle(AddUserCarCommand request, CancellationToken cancellationToken)
    {
        var userCar = new UserCar()
        {
            UserId = request.UserId,
            CarId = request.CarId,
            PlugTypeId = request.PlugTypeId
        };
        applicationDbContext.UserCars.Add(userCar);
        await applicationDbContext.SaveChanges(cancellationToken);
        return userCar.Id;
    }
}