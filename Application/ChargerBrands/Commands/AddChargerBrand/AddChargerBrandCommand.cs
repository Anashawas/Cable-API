using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargerBrands.Commands.AddChargerBrand;

public record AddChargerBrandCommand(string Name) : IRequest<int>;

public class AddChargerBrandCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddChargerBrandCommand, int>
{
    public async Task<int> Handle(AddChargerBrandCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        var exists = await applicationDbContext.ChargerBrands
            .AnyAsync(x => x.Name == name, cancellationToken);
        if (exists)
            throw new DataValidationException("Name", $"Charger brand '{name}' already exists");

        var brand = new ChargerBrand { Name = name };
        applicationDbContext.ChargerBrands.Add(brand);
        await applicationDbContext.SaveChanges(cancellationToken);
        return brand.Id;
    }
}
