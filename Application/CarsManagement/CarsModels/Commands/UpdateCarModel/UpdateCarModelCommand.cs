using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Commands.UpdateCarModel;

public record UpdateCarModelCommand(int Id, string Name, int CarTypeId) : IRequest;

public class UpdateCarModelCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateCarModelCommand>
{
    public async Task Handle(UpdateCarModelCommand request, CancellationToken cancellationToken)
    {
        var carModel =
            await applicationDbContext.CarModels.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken) ??
            throw new NotFoundException($"can not find CarModel with Id {request.Id}");
        carModel.Name = request.Name;
        carModel.CarTypeId = request.CarTypeId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}