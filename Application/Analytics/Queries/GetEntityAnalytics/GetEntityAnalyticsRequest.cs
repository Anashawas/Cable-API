using Application.Analytics;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Analytics.Queries.GetEntityAnalytics;

public record AnalyticsEventTotalDto(int EventType, string EventName, int TotalCount, int UniqueUsers);

public record AnalyticsDailyCountDto(DateTime Day, int EventType, string EventName, int Count);

public record EntityAnalyticsDto(
    string EntityType,
    int EntityId,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<AnalyticsEventTotalDto> Totals,
    IReadOnlyList<AnalyticsDailyCountDto> Daily);

/// <summary>
/// Analytics dashboard for a single provider or banner: per-event totals,
/// unique users and a daily time-series. Restricted to the provider owner
/// (banners require an authenticated caller).
/// </summary>
public record GetEntityAnalyticsRequest(
    string EntityType,
    int EntityId,
    DateTime? From,
    DateTime? To) : IRequest<EntityAnalyticsDto>;

public class GetEntityAnalyticsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetEntityAnalyticsRequest, EntityAnalyticsDto>
{
    public async Task<EntityAnalyticsDto> Handle(GetEntityAnalyticsRequest request, CancellationToken cancellationToken)
    {
        var entityType = request.EntityType;

        if (!AnalyticsCatalog.IsKnownEntityType(entityType))
            throw new DataValidationException("EntityType",
                "EntityType must be one of: ChargingPoint, ServiceProvider, Banner.");

        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        await EnsureAccessAsync(entityType, request.EntityId, callerId, cancellationToken);

        // Resolve the date window (defaults to the last 30 days).
        var now = DateTime.UtcNow;
        var to = request.To ?? now;
        var from = request.From ?? to.AddDays(-30);
        var fromDate = from.Date;
        var toDate = to.Date;
        var toExclusive = toDate.AddDays(1);

        // Totals + daily series come from the pre-aggregated rollup (fast).
        var rollups = await applicationDbContext.AnalyticsDailyRollups
            .Where(r => r.EntityType == entityType
                        && r.EntityId == request.EntityId
                        && r.Day >= fromDate
                        && r.Day <= toDate)
            .OrderBy(r => r.Day)
            .Select(r => new { r.EventType, r.Day, r.Count })
            .ToListAsync(cancellationToken);

        // Unique users come from the raw log — distinct (event, user) pairs.
        var uniquePairs = await applicationDbContext.AnalyticsEvents
            .Where(e => e.EntityType == entityType
                        && e.EntityId == request.EntityId
                        && e.OccurredAt >= fromDate
                        && e.OccurredAt < toExclusive
                        && e.UserId != null)
            .Select(e => new { e.EventType, e.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var uniquePerEvent = uniquePairs
            .GroupBy(p => p.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

        var daily = rollups
            .Select(r => new AnalyticsDailyCountDto(
                r.Day, r.EventType, EventName(r.EventType), r.Count))
            .ToList();

        var totals = rollups
            .GroupBy(r => r.EventType)
            .Select(g => new AnalyticsEventTotalDto(
                g.Key,
                EventName(g.Key),
                g.Sum(x => x.Count),
                uniquePerEvent.TryGetValue(g.Key, out var u) ? u : 0))
            .OrderBy(t => t.EventType)
            .ToList();

        return new EntityAnalyticsDto(entityType, request.EntityId, from, to, totals, daily);
    }

    private async Task EnsureAccessAsync(
        string entityType, int entityId, int callerId, CancellationToken cancellationToken)
    {
        if (AnalyticsCatalog.IsProviderType(entityType))
        {
            var provider = entityType == AnalyticsCatalog.ChargingPoint
                ? await applicationDbContext.ChargingPoints
                    .Where(x => x.Id == entityId && !x.IsDeleted)
                    .Select(x => new { x.OwnerId })
                    .FirstOrDefaultAsync(cancellationToken)
                : await applicationDbContext.ServiceProviders
                    .Where(x => x.Id == entityId && !x.IsDeleted)
                    .Select(x => new { x.OwnerId })
                    .FirstOrDefaultAsync(cancellationToken);

            if (provider is null)
                throw new NotFoundException($"{entityType} with id {entityId} not found");

            // Owner OR admin — admins can view any provider's analytics
            // (including unassigned providers, which have no owner to ask).
            var isOwner = provider.OwnerId is not null && provider.OwnerId.Value == callerId;
            if (!isOwner && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
                throw new ForbiddenAccessException("You can only view analytics for providers you own.");
        }
        else // Banner — internal/admin surface; an authenticated caller is enough.
        {
            var exists = await applicationDbContext.Banners
                .AnyAsync(x => x.Id == entityId && !x.IsDeleted, cancellationToken);
            if (!exists)
                throw new NotFoundException($"Banner with id {entityId} not found");
        }
    }

    private static string EventName(int eventType) =>
        ((AnalyticsEventType)eventType).ToString();
}
