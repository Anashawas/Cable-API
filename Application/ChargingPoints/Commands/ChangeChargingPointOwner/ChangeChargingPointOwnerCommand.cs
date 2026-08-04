using Application.Providers;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.ChangeChargingPointOwner;

/// <summary>NewOwnerId null = unassign the station (blocked while it has active offers / partner agreements).</summary>
public record ChangeChargingPointOwnerCommand(int ChargingPointId, int? NewOwnerId) : IRequest;

public class ChangeChargingPointOwnerCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<ChangeChargingPointOwnerCommand>
{
    public async Task Handle(ChangeChargingPointOwnerCommand request, CancellationToken cancellationToken)
    {
        var chargingPoint = await applicationDbContext.ChargingPoints
                                .FirstOrDefaultAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, cancellationToken)
                            ?? throw new NotFoundException($"Cannot find charging point with id {request.ChargingPointId}");

        if (chargingPoint.OwnerId == request.NewOwnerId)
            throw new DataValidationException("NewOwnerId", request.NewOwnerId.HasValue
                ? "New owner is already the current owner of this charging point"
                : "This charging point is already unassigned");

        if (request.NewOwnerId.HasValue)
        {
            var newOwner = await applicationDbContext.UserAccounts
                               .FirstOrDefaultAsync(x => x.Id == request.NewOwnerId.Value && !x.IsDeleted, cancellationToken)
                           ?? throw new NotFoundException($"Cannot find user with id {request.NewOwnerId}");

            if (newOwner.RoleId != 4)
                throw new DataValidationException("NewOwnerId", "New owner must have Provider role");
        }
        else
        {
            await ProviderUnassignGuard.EnsureCanUnassignAsync(
                applicationDbContext, "ChargingPoint", chargingPoint.Id, cancellationToken);
        }

        chargingPoint.OwnerId = request.NewOwnerId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
