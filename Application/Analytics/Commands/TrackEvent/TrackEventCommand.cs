using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Analytics.Commands.TrackEvent;

/// <summary>
/// Records a single analytics interaction. This is the engine's public front door —
/// the client calls it for every tracked event (Full/Half view, Call, Map click,
/// Banner view/click). Anonymous requests are allowed; UserId is captured when present.
/// </summary>
public record TrackEventCommand(
    string EntityType,
    int EntityId,
    AnalyticsEventType EventType,
    string? AnonymousId = null,
    string? Source = null,
    string? City = null,
    double? Lat = null,
    double? Lng = null) : IRequest;

public class TrackEventCommandHandler(
    IApplicationDbContext applicationDbContext,
    IAnalyticsService analyticsService,
    ICurrentUserService currentUserService)
    : IRequestHandler<TrackEventCommand>
{
    public async Task Handle(TrackEventCommand request, CancellationToken cancellationToken)
    {
        // Reject analytics for entities that don't exist so we never record orphans.
        var exists = await EntityExistsAsync(request.EntityType, request.EntityId, cancellationToken);
        if (!exists)
            throw new NotFoundException($"{request.EntityType} with id {request.EntityId} not found");

        await analyticsService.TrackAsync(
            request.EntityType,
            request.EntityId,
            request.EventType,
            currentUserService.UserId,
            request.AnonymousId,
            request.Source,
            request.City,
            request.Lat,
            request.Lng,
            cancellationToken);
    }

    private async Task<bool> EntityExistsAsync(string entityType, int entityId, CancellationToken ct) =>
        entityType switch
        {
            AnalyticsCatalog.ChargingPoint => await applicationDbContext.ChargingPoints
                .AnyAsync(x => x.Id == entityId && !x.IsDeleted, ct),
            AnalyticsCatalog.ServiceProvider => await applicationDbContext.ServiceProviders
                .AnyAsync(x => x.Id == entityId && !x.IsDeleted, ct),
            AnalyticsCatalog.Banner => await applicationDbContext.Banners
                .AnyAsync(x => x.Id == entityId && !x.IsDeleted, ct),
            AnalyticsCatalog.Announcement => await applicationDbContext.Announcements
                .AnyAsync(x => x.Id == entityId && !x.IsDeleted, ct),
            _ => false
        };
}
