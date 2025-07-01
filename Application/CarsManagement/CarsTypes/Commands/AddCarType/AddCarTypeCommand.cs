namespace Application.CarsManagement.CarsTypes.Commands.AddCarTypeCommand;

public record AddCarTypeCommand(string Name) :IRequest<int>;

public class AddCarTypeCommandHandler(IApplicationDbContext applicationDbContext):IRequestHandler<AddCarTypeCommand, int>
{
    public async Task<int> Handle(AddCarTypeCommand request, CancellationToken cancellationToken)
    {
        var carType = new CarType()
        {
            Name = request.Name,
        };
         applicationDbContext.CarTypes.Add(carType);
         await applicationDbContext.SaveChanges(cancellationToken);
         return carType.Id;
    }
}