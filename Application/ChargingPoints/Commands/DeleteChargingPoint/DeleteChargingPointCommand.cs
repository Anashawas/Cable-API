using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.DeleteChargingPoint;

public record DeleteChargingPointCommand(int Id) : IRequest;

public class DeleteChargingPointCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteChargingPointCommand>
{
    public async Task Handle(DeleteChargingPointCommand request, CancellationToken cancellationToken)
    {
        var chargingPoint = await applicationDbContext.ChargingPoints
                                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.Id, cancellationToken)
                            ?? throw new NotFoundException($"can not find charging point with id {request.Id}");
        
        chargingPoint.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}