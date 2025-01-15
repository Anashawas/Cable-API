namespace Application.PlugTypes.Commands.AddPlugType;

public record AddPlugTypeCommand(string? Name, string SerialNumber) : IRequest<int>;

public class AddPlugTypeCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddPlugTypeCommand, int>
{
    public async Task<int> Handle(AddPlugTypeCommand request, CancellationToken cancellationToken)
    {
        var plugType = new PlugType
        {
            Name = request.Name,
            SerialNumber = request.SerialNumber
        };
        
        applicationDbContext.PlugTypes.Add(plugType);
        await applicationDbContext.SaveChanges(cancellationToken);

        return plugType.Id;
    }
}