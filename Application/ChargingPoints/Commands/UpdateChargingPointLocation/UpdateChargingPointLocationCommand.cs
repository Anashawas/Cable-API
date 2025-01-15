using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UpdateChargingPointLocation;

public record UpdateChargingPointLocationCommand(int Id, double Latitude, double Longitude) : IRequest;


public class UpdateChargingPointLocationCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateChargingPointLocationCommand>
{
    public async Task Handle(UpdateChargingPointLocationCommand request, CancellationToken cancellationToken)
    {
        var chargingPoint =
            await applicationDbContext.ChargingPoints.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"can not find charging point with id {request.Id}");
        chargingPoint.Latitude = request.Latitude;
        chargingPoint.Longitude = request.Longitude;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

