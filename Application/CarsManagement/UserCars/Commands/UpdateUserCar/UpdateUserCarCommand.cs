using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Commands.UpdateUserCar;

public record UpdateUserCarCommand(int Id, int CarModelId, int PlugTypeId) : IRequest;

public class UpdateUserCarCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateUserCarCommand>
{
    public async Task Handle(UpdateUserCarCommand request, CancellationToken cancellationToken)
    {
        var userCar =
            await applicationDbContext.UserCars.FirstOrDefaultAsync(
                x => x.Id == request.Id , cancellationToken)
            ?? throw new NotFoundException($"can not find user car with id {request.Id} and id {request.CarModelId}");
        userCar.CarModelId = request.CarModelId;
        userCar.PlugTypeId = request.PlugTypeId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}