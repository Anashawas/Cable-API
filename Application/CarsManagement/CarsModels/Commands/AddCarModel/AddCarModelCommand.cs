namespace Application.CarsManagement.CarsModels.Commands.AddCarModal;

public record AddCarModelCommand(string Name,int CarTypeId):IRequest<int>;
public class AddCarModelCommandHandler(IApplicationDbContext applicationDbContext) :  IRequestHandler<AddCarModelCommand, int>
{
    public async Task<int> Handle(AddCarModelCommand request, CancellationToken cancellationToken)
    {
        var carModel = new CarModel()
        {
            Name = request.Name,
            CarTypeId = request.CarTypeId
        };
        applicationDbContext.CarModels.Add(carModel);
        await applicationDbContext.SaveChanges(cancellationToken);
        return carModel.Id;
    }
}


