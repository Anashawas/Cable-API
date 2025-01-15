using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UpdateChargingPointVisitorsCount;

public record UpdateChargingPointVisitorsCountCommand(int Id) : IRequest;

public class UpdateChargingPointVisitorsCountCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateChargingPointVisitorsCountCommand>
{
    public async Task Handle(UpdateChargingPointVisitorsCountCommand request, CancellationToken cancellationToken)
    {
        var chargingPoint =
            await applicationDbContext.ChargingPoints.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"can not find charging point with id {request.Id}");
        chargingPoint.VisitorsCount += 1;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}