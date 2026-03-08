using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.ChangeChargingPointOwner;

public record ChangeChargingPointOwnerCommand(int ChargingPointId, int NewOwnerId) : IRequest;

public class ChangeChargingPointOwnerCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<ChangeChargingPointOwnerCommand>
{
    public async Task Handle(ChangeChargingPointOwnerCommand request, CancellationToken cancellationToken)
    {
        var chargingPoint = await applicationDbContext.ChargingPoints
                                .FirstOrDefaultAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, cancellationToken)
                            ?? throw new NotFoundException($"Cannot find charging point with id {request.ChargingPointId}");

        var newOwner = await applicationDbContext.UserAccounts
                           .FirstOrDefaultAsync(x => x.Id == request.NewOwnerId && !x.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Cannot find user with id {request.NewOwnerId}");

        chargingPoint.OwnerId = request.NewOwnerId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
