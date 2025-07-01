using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Commands.UpdateUserCar;

public record UpdateUserCarCommand(int UserId, int CarId, int PlugTypeId) : IRequest;

public class UpdateUserCarCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateUserCarCommand>
{
    public async Task Handle(UpdateUserCarCommand request, CancellationToken cancellationToken)
    {
        var userCar =
            await applicationDbContext.UserCars.FirstOrDefaultAsync(
                x => x.Id == request.UserId && x.CarId == request.CarId, cancellationToken)
            ?? throw new NotFoundException($"can not find user car with id {request.UserId} and id {request.CarId}");
        userCar.UserId = request.UserId;
        userCar.CarId = request.CarId;
        userCar.PlugTypeId = request.PlugTypeId;
        await applicationDbContext.SaveChanges(cancellationToken)
            ;
    }
}