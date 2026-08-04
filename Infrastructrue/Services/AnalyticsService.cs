using Application.Analytics;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructrue.Services;

/// <summary>
/// The analytics engine. Records a raw event, atomically bumps the daily rollup,
/// and keeps the legacy provider VisitorsCount in sync for full views.
/// Fully resilient — any failure is logged and swallowed so analytics can never
/// break the user's actual request.
/// </summary>
public class AnalyticsService(
    IApplicationDbContext applicationDbContext,
    ILogger<AnalyticsService> logger) : IAnalyticsService
{
    /// <summary>One impression per viewer per asset per this window (ad-billing honesty).
    /// 30 seconds per the client decision (§10.1): kills same-screen carousel re-fires
    /// while still counting genuinely separate visits.</summary>
    private static readonly TimeSpan ImpressionDedupWindow = TimeSpan.FromSeconds(30);

    private static readonly AnalyticsEventType[] ImpressionEvents =
    {
        AnalyticsEventType.BannerView,   // banner + announcement views
        AnalyticsEventType.PremiumView
    };

    public async Task TrackAsync(
        string entityType,
        int entityId,
        AnalyticsEventType eventType,
        int? userId = null,
        string? anonymousId = null,
        string? source = null,
        string? city = null,
        double? lat = null,
        double? lng = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;

            // 0. Impression dedup: the carousel refires a view every time a slot
            //    re-enters visibility — count at most one per viewer per window.
            if (ImpressionEvents.Contains(eventType) && (userId is not null || anonymousId is not null))
            {
                var since = now - ImpressionDedupWindow;
                var duplicate = await applicationDbContext.AnalyticsEvents.AsNoTracking()
                    .AnyAsync(e => e.EntityType == entityType
                                   && e.EntityId == entityId
                                   && e.EventType == (int)eventType
                                   && e.OccurredAt >= since
                                   && (userId != null ? e.UserId == userId : e.AnonymousId == anonymousId),
                        cancellationToken);
                if (duplicate)
                    return;
            }

            // 1. Append the raw event (immutable log — source of truth).
            applicationDbContext.AnalyticsEvents.Add(new AnalyticsEvent
            {
                EntityType = entityType,
                EntityId = entityId,
                EventType = (int)eventType,
                UserId = userId,
                AnonymousId = anonymousId,
                Source = source,
                City = city,
                Lat = lat,
                Lng = lng,
                OccurredAt = now
            });
            await applicationDbContext.SaveChanges(cancellationToken);

            // 2. Atomically bump the pre-aggregated daily counter.
            await UpsertDailyRollupAsync(entityType, entityId, (int)eventType, now, cancellationToken);

            // 3. Keep the legacy denormalized VisitorsCount in sync for provider full views.
            if (eventType == AnalyticsEventType.FullView && AnalyticsCatalog.IsProviderType(entityType))
                await BumpVisitorsCountAsync(entityType, entityId, cancellationToken);

            // 4. Welcome-message dismiss feeds the stop-on-dismiss frequency cap.
            if (entityType == AnalyticsCatalog.Announcement
                && eventType == AnalyticsEventType.Dismiss && userId is not null)
                await BumpAnnouncementDismissAsync(entityId, userId.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            // Analytics is best-effort — never surface a failure to the caller.
            logger.LogWarning(ex,
                "Failed to track analytics event {EventType} for {EntityType}/{EntityId}",
                eventType, entityType, entityId);
        }
    }

    private async Task BumpAnnouncementDismissAsync(int announcementId, int userId, CancellationToken ct)
    {
        var updated = await applicationDbContext.AnnouncementUserStates
            .Where(s => s.AnnouncementId == announcementId && s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DismissedCount, x => x.DismissedCount + 1), ct);

        if (updated == 0)
        {
            applicationDbContext.AnnouncementUserStates.Add(new AnnouncementUserState
            {
                AnnouncementId = announcementId,
                UserId = userId,
                DismissedCount = 1
            });
            await applicationDbContext.SaveChanges(ct);
        }
    }

    /// <summary>
    /// Concurrency-safe daily upsert via MERGE + HOLDLOCK. Avoids EF change-tracking
    /// and the read-then-write race of a separate check + insert.
    /// </summary>
    private async Task UpsertDailyRollupAsync(
        string entityType, int entityId, int eventType, DateTime now,
        CancellationToken cancellationToken)
    {
        var day = now.Date;

        await applicationDbContext.Database.ExecuteSqlInterpolatedAsync($@"
MERGE [dbo].[AnalyticsDailyRollup] WITH (HOLDLOCK) AS target
USING (SELECT {entityType} AS EntityType, {entityId} AS EntityId, {eventType} AS EventType, CAST({day} AS date) AS [Day]) AS src
    ON target.[EntityType] = src.EntityType
   AND target.[EntityId]   = src.EntityId
   AND target.[EventType]  = src.EventType
   AND target.[Day]        = src.[Day]
WHEN MATCHED THEN
    UPDATE SET target.[Count] = target.[Count] + 1, target.[LastEventAt] = {now}
WHEN NOT MATCHED THEN
    INSERT ([EntityType], [EntityId], [EventType], [Day], [Count], [LastEventAt])
    VALUES (src.EntityType, src.EntityId, src.EventType, src.[Day], 1, {now});", cancellationToken);
    }

    private async Task BumpVisitorsCountAsync(
        string entityType, int entityId, CancellationToken cancellationToken)
    {
        if (entityType == AnalyticsCatalog.ChargingPoint)
            await applicationDbContext.ChargingPoints
                .Where(x => x.Id == entityId && !x.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.VisitorsCount, c => c.VisitorsCount + 1), cancellationToken);
        else if (entityType == AnalyticsCatalog.ServiceProvider)
            await applicationDbContext.ServiceProviders
                .Where(x => x.Id == entityId && !x.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.VisitorsCount, c => c.VisitorsCount + 1), cancellationToken);
    }
}
