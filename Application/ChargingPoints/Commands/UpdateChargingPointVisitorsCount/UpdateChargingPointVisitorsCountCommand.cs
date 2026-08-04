using Application.Analytics;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UpdateChargingPointVisitorsCount;

public record UpdateChargingPointVisitorsCountCommand(int Id) : IRequest;

public class UpdateChargingPointVisitorsCountCommandHandler(
    IApplicationDbContext applicationDbContext,
    IAnalyticsService analyticsService,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateChargingPointVisitorsCountCommand>
{
    public async Task Handle(UpdateChargingPointVisitorsCountCommand request, CancellationToken cancellationToken)
    {
        var exists = await applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);

        if (!exists)
            throw new NotFoundException($"can not find charging point with id {request.Id}");

        // Records a FullView; the analytics engine also keeps the legacy
        // VisitorsCount column in sync (backward compatible).
        await analyticsService.TrackAsync(
            AnalyticsCatalog.ChargingPoint, request.Id, AnalyticsEventType.FullView,
            currentUserService.UserId, cancellationToken: cancellationToken);
    }
}