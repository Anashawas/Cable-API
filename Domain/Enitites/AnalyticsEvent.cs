using System;
using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Append-only analytics event log — one row per tracked interaction
/// (view, call, map click, banner click, ...). Polymorphic via
/// (<see cref="EntityType"/>, <see cref="EntityId"/>) so a single table serves
/// charging points, service providers and banners. Never updated or deleted.
/// </summary>
public class AnalyticsEvent : BaseEntity
{
    /// <summary>"ChargingPoint", "ServiceProvider" or "Banner".</summary>
    public string EntityType { get; set; } = null!;

    /// <summary>Id of the charging point / service provider / banner.</summary>
    public int EntityId { get; set; }

    /// <summary>Numeric value of Cable.Core.Emuns.AnalyticsEventType.</summary>
    public int EventType { get; set; }

    /// <summary>Authenticated user who triggered the event; null when anonymous.</summary>
    public int? UserId { get; set; }

    /// <summary>Client-supplied device/session id, used to count unique anonymous traffic.</summary>
    public string? AnonymousId { get; set; }

    /// <summary>Optional platform tag: "iOS", "Android", "Web".</summary>
    public string? Source { get; set; }

    /// <summary>Where the user was when the event fired (proves geo delivery to advertisers).</summary>
    public string? City { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime OccurredAt { get; set; }
}
