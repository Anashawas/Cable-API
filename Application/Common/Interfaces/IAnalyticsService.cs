using Cable.Core.Emuns;

namespace Application.Common.Interfaces;

/// <summary>
/// Central analytics engine. One entry point that every event source calls to
/// record an interaction. Adding a new event type is an enum change — no new
/// plumbing. Implementations are resilient: a tracking failure is logged and
/// swallowed so it can never break the caller's request.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Records a single analytics event (append-only) and atomically bumps the
    /// daily rollup counter. For provider full-views it also keeps the legacy
    /// denormalized VisitorsCount column in sync. Persists internally; never throws.
    /// </summary>
    Task TrackAsync(
        string entityType,
        int entityId,
        AnalyticsEventType eventType,
        int? userId = null,
        string? anonymousId = null,
        string? source = null,
        string? city = null,
        double? lat = null,
        double? lng = null,
        CancellationToken cancellationToken = default);
}
