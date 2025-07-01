using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.Cars.Commands.DeleteCar;

public record DeleteCarCommand(int Id) : IRequest;

public class DeleteCarCommandHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<DeleteCarCommand>
{
    public async Task Handle(DeleteCarCommand request, CancellationToken cancellationToken)
    {
        var car = await applicationDbContext.Cars.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken) ??
                  throw new NotFoundException("Car not found");
        applicationDbContext.Cars.Remove(car);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}