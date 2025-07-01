using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsTypes.Commands.UpdateCarType;

public record UpdateCarTypeCommand(int Id, string Name) : IRequest;

public class UpdateCarTypeCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateCarTypeCommand>
{
    public async Task Handle(UpdateCarTypeCommand request, CancellationToken cancellationToken)
    {
        var carType =
            await applicationDbContext.CarTypes.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"can not find car type with id {request.Id}");
        carType.Name = request.Name;
        await applicationDbContext.SaveChanges(cancellationToken)
            ;
    }
}