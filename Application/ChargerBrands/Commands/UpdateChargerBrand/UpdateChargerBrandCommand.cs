using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargerBrands.Commands.UpdateChargerBrand;

public record UpdateChargerBrandCommand(int Id, string Name) : IRequest;

public class UpdateChargerBrandCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateChargerBrandCommand>
{
    public async Task Handle(UpdateChargerBrandCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        var brand = await applicationDbContext.ChargerBrands
                        .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                    ?? throw new NotFoundException($"can not find charger brand with id {request.Id}");

        var duplicate = await applicationDbContext.ChargerBrands
            .AnyAsync(x => x.Name == name && x.Id != request.Id, cancellationToken);
        if (duplicate)
            throw new DataValidationException("Name", $"Charger brand '{name}' already exists");

        brand.Name = name;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
