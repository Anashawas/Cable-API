using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Commands.DeleteUserCar;

public record DeleteUserCarCommand(int Id) : IRequest;

public class DeleteUserCarCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteUserCarCommand>
{
    public async Task Handle(DeleteUserCarCommand request, CancellationToken cancellationToken)
    {
        var userCar =
                await applicationDbContext.UserCars.FirstOrDefaultAsync(x => x.Id == request.Id,
                    cancellationToken: cancellationToken)
                ?? throw new NotFoundException()
            ;

        applicationDbContext.UserCars.Remove(userCar);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}