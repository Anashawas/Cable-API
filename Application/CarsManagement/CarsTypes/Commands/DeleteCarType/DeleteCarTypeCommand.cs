using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsTypes.Commands.DeleteCarType;

public record DeleteCarTypeCommand(int Id) : IRequest;

public class DeleteCarTypeCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteCarTypeCommand>
{
    public async Task Handle(DeleteCarTypeCommand request, CancellationToken cancellationToken)
    {
        var carType =
                await applicationDbContext.CarTypes.FirstOrDefaultAsync(x => x.Id == request.Id,
                    cancellationToken: cancellationToken)
                ?? throw new NotFoundException()
            ;

        applicationDbContext.CarTypes.Remove(carType);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}