using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Commands.AddCarModal;

public record AddCarModelCommand(string Name,int CarTypeId, int? SizeId = null):IRequest<int>;
public class AddCarModelCommandHandler(IApplicationDbContext applicationDbContext) :  IRequestHandler<AddCarModelCommand, int>
{
    public async Task<int> Handle(AddCarModelCommand request, CancellationToken cancellationToken)
    {
        if (request.SizeId.HasValue &&
            !await applicationDbContext.CarModelSizes.AnyAsync(x => x.Id == request.SizeId.Value, cancellationToken))
            throw new NotFoundException($"can not find car model size with id {request.SizeId}");

        var carModel = new CarModel()
        {
            Name = request.Name,
            CarTypeId = request.CarTypeId,
            SizeId = request.SizeId
        };
        applicationDbContext.CarModels.Add(carModel);
        await applicationDbContext.SaveChanges(cancellationToken);
        return carModel.Id;
    }
}


