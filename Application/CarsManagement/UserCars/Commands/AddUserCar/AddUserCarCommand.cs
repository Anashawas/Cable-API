namespace Application.CarsManagement.UserCars.Commands.AddUserCar;

public record AddUserCarCommand(int UserId, int CarModelId, int PlugTypeId) : IRequest<int>;

public class AddUserCarCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddUserCarCommand, int>
{
    public async Task<int> Handle(AddUserCarCommand request, CancellationToken cancellationToken)
    {
        var userCar = new UserCar()
        {
            UserId = request.UserId,
            CarModelId = request.CarModelId,
            PlugTypeId = request.PlugTypeId
        };
        applicationDbContext.UserCars.Add(userCar);
        await applicationDbContext.SaveChanges(cancellationToken);
        return userCar.Id;
    }
}