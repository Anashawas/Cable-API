using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargerBrands.Commands.DeleteChargerBrand;

public record DeleteChargerBrandCommand(int Id) : IRequest;

public class DeleteChargerBrandCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteChargerBrandCommand>
{
    public async Task Handle(DeleteChargerBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await applicationDbContext.ChargerBrands
                        .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                    ?? throw new NotFoundException($"can not find charger brand with id {request.Id}");

        var inUse = await applicationDbContext.ChargingPointChargerBrands
            .AnyAsync(x => x.ChargerBrandId == request.Id && !x.ChargingPoint.IsDeleted, cancellationToken);
        if (inUse)
            throw new DataValidationException("ChargerBrand",
                "This brand is used by one or more charging points and cannot be deleted");

        applicationDbContext.ChargerBrands.Remove(brand);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
