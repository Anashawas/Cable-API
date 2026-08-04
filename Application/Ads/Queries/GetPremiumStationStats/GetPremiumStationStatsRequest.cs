using Application.Analytics;
using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ads.Queries.GetPremiumStationStats;

public record PremiumStationStatsDto(int StationId, int Impressions, int Clicks, double Ctr);

/// <summary>
/// "Seen as a paid ad on home" report for one premium station (events 30/31),
/// separate from its normal map/details analytics. Owner, worker, or admin.
/// </summary>
public record GetPremiumStationStatsRequest(int StationId, DateTime? From = null, DateTime? To = null)
    : IRequest<PremiumStationStatsDto>;

public class GetPremiumStationStatsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPremiumStationStatsRequest, PremiumStationStatsDto>
{
    public async Task<PremiumStationStatsDto> Handle(GetPremiumStationStatsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var ownerId = await applicationDbContext.ChargingPoints.AsNoTracking()
                          .Where(x => x.Id == request.StationId && !x.IsDeleted)
                          .Select(x => new { x.OwnerId })
                          .FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException($"can not find charging point with id {request.StationId}");

        var isWorker = await applicationDbContext.ProviderManagers.AsNoTracking()
            .AnyAsync(pm => pm.ProviderType == "ChargingPoint" && pm.ProviderId == request.StationId
                            && pm.UserId == userId && pm.IsActive && !pm.IsDeleted, cancellationToken);

        if (ownerId.OwnerId != userId && !isWorker
            && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
            throw new ForbiddenAccessException("Only the station owner, its worker, or an admin can view premium stats.");

        var from = (request.From ?? DateTime.UtcNow.AddDays(-30)).Date;
        var to = (request.To ?? DateTime.UtcNow).Date;

        var rollups = await applicationDbContext.AnalyticsDailyRollups.AsNoTracking()
            .Where(r => r.EntityType == AnalyticsCatalog.ChargingPoint
                        && r.EntityId == request.StationId
                        && r.Day >= from && r.Day <= to
                        && (r.EventType == (int)AnalyticsEventType.PremiumView
                            || r.EventType == (int)AnalyticsEventType.PremiumClick))
            .GroupBy(r => r.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Sum(x => x.Count) })
            .ToListAsync(cancellationToken);

        var impressions = rollups.FirstOrDefault(r => r.EventType == (int)AnalyticsEventType.PremiumView)?.Count ?? 0;
        var clicks = rollups.FirstOrDefault(r => r.EventType == (int)AnalyticsEventType.PremiumClick)?.Count ?? 0;

        return new PremiumStationStatsDto(
            request.StationId,
            impressions,
            clicks,
            impressions > 0 ? Math.Round((double)clicks / impressions, 4) : 0);
    }
}
