using System;
using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Pre-aggregated daily counter, one row per
/// (<see cref="EntityType"/>, <see cref="EntityId"/>, <see cref="EventType"/>, <see cref="Day"/>).
/// Incremented atomically on each event so dashboards read totals in O(1)
/// instead of scanning the raw <see cref="AnalyticsEvent"/> log. Unique-visitor
/// metrics are computed on demand from the raw log, not stored here.
/// </summary>
public class AnalyticsDailyRollup : BaseEntity
{
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }
    public int EventType { get; set; }

    /// <summary>The UTC calendar day (date only) these events fall on.</summary>
    public DateTime Day { get; set; }

    public int Count { get; set; }

    /// <summary>UTC timestamp of the most recent event folded into this bucket.</summary>
    public DateTime LastEventAt { get; set; }
}
