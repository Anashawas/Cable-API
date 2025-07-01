using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Commands.DeleteCarModel;

public record DeleteCarModelCommand(int Id) : IRequest;

public class DeleteCarModelCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteCarModelCommand>
{
    public async Task Handle(DeleteCarModelCommand request, CancellationToken cancellationToken)
    {
        var carModel =
            await applicationDbContext.CarModels.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"can not find CarModel with Id {request.Id}");
        applicationDbContext.CarModels.Remove(carModel);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}