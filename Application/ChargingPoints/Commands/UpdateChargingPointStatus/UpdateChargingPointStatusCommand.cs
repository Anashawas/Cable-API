using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UpdateChangingPointStatus;

public record UpdateChargingPointStatusCommand(int Id, int StatusId) : IRequest;

public class UpdateChangingPointStatusCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateChargingPointStatusCommand>
{
    public async Task Handle(UpdateChargingPointStatusCommand request, CancellationToken cancellationToken)
    {
        var chargingPoint = await applicationDbContext.ChargingPoints
                                .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                            ?? throw new NotFoundException($"can not find charging point with id {request.Id}");
        
        chargingPoint.StatusId = request.StatusId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}