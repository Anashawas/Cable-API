using Application.Common.Interfaces;
using Application.Settings;
using Application.Subscriptions;
using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp;

/// <summary>Whether a station's Cable Connect subscription currently shows charger data.</summary>
public record OcppSubscriptionStateDto(bool IsOn, string? Status, DateTime? ExpiresAt, int? SubscriptionId);

/// <summary>
/// The one place that decides "does this station get to see its chargers right now".
/// Read by the admin list, and by the provider and B2C queries that hide data when
/// the subscription lapses. It never influences Cable.Ocpp — the charger keeps being
/// answered regardless, because cutting it off could stop cars from charging.
/// </summary>
public static class OcppSubscriptionGate
{
    public static async Task<Dictionary<int, OcppSubscriptionStateDto>> GetStatesAsync(
        IApplicationDbContext db, IEnumerable<int> chargingPointIds, CancellationToken ct)
    {
        var ids = chargingPointIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, OcppSubscriptionStateDto>();

        var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(db, ct);
        var now = DateTime.UtcNow;

        var subs = await db.Subscriptions.AsNoTracking()
            .Where(s => s.EntityType == SubscriptionEntityTypes.OcppConnect && ids.Contains(s.EntityId) && !s.IsDeleted)
            .ToListAsync(ct);

        var none = new OcppSubscriptionStateDto(false, null, null, null);
        return ids.ToDictionary(id => id, id =>
        {
            var s = subs.FirstOrDefault(x => x.EntityId == id);
            if (s is null) return none;
            var effective = SubscriptionPeriodCalculator.EffectiveGrace(s, grace);
            return new OcppSubscriptionStateDto(
                SubscriptionPeriodCalculator.IsOn(s, effective, now),
                SubscriptionPeriodCalculator.StatusOf(s, effective, now),
                s.ExpiresAt,
                s.Id);
        });
    }

    public static async Task<OcppSubscriptionStateDto> GetStateAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
        => (await GetStatesAsync(db, [chargingPointId], ct))[chargingPointId];

    public static async Task<bool> IsOnAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
        => (await GetStateAsync(db, chargingPointId, ct)).IsOn;
}

/// <summary>
/// N-2: the four gates between a station's live charger data and a driver's screen.
/// Freshness is per charger and is checked by the caller (OcppLiveness); this answers the
/// three station-level ones. The driver app's remote-config kill switch is client-side.
/// </summary>
public record OcppLiveVisibilityDto(
    bool VisibleToDrivers,
    bool SubscriptionOn,
    bool OwnerSharing,
    DateTime? OwnerDecidedAt,
    bool AdminBlocked,
    DateTime? AdminBlockedAt,
    string? AdminBlockReason);

public static class OcppLiveVisibility
{
    public static async Task<Dictionary<int, OcppLiveVisibilityDto>> GetAsync(IApplicationDbContext db, IEnumerable<int> chargingPointIds, CancellationToken ct)
    {
        var ids = chargingPointIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, OcppLiveVisibilityDto>();

        var subs = await OcppSubscriptionGate.GetStatesAsync(db, ids, ct);
        var stations = await db.ChargingPoints.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ShareLiveStatus, c.ShareLiveStatusSetAt, c.LiveStatusBlocked, c.LiveStatusBlockedAt, c.LiveStatusBlockReason })
            .ToListAsync(ct);

        return ids.ToDictionary(id => id, id =>
        {
            var st = stations.FirstOrDefault(x => x.Id == id);
            var subOn = subs[id].IsOn;
            var sharing = st?.ShareLiveStatus ?? false;
            var blocked = st?.LiveStatusBlocked ?? false;
            return new OcppLiveVisibilityDto(subOn && sharing && !blocked, subOn, sharing, st?.ShareLiveStatusSetAt, blocked, st?.LiveStatusBlockedAt, st?.LiveStatusBlockReason);
        });
    }

    public static async Task<OcppLiveVisibilityDto> GetAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
        => (await GetAsync(db, [chargingPointId], ct))[chargingPointId];

    /// <summary>The single yes/no the B2C queries use.</summary>
    public static async Task<bool> IsVisibleAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
        => (await GetAsync(db, chargingPointId, ct)).VisibleToDrivers;
}

/// <summary>
/// Three display states, derived from the charger row the OCPP process keeps up to date.
/// "Connected" alone is not enough: if Cable.Ocpp dies hard (host recycle) the flag
/// stays true, so freshness of LastMessageAt is what actually says "online".
/// </summary>
public static class OcppLiveness
{
    public const string Online = "Online";
    public const string Reconnecting = "Reconnecting";
    public const string Offline = "Offline";

    /// <summary>A short gap is normal: our recycles, the charger's reboots. Longer than this reads as offline.</summary>
    public static readonly TimeSpan ReconnectingWindow = TimeSpan.FromMinutes(2);

    /// <summary>LastMessageAt is persisted at most once a minute; heartbeats come every HeartbeatInterval.</summary>
    public static TimeSpan FreshnessFor(int heartbeatInterval) =>
        TimeSpan.FromSeconds(Math.Max(180, heartbeatInterval * 2.5));

    public static string StateOf(bool isConnected, DateTime? lastMessageAt, DateTime? disconnectedAt, int heartbeatInterval, DateTime nowUtc)
    {
        if (isConnected && lastMessageAt is { } lm && nowUtc - lm <= FreshnessFor(heartbeatInterval))
            return Online;

        var lastSeen = disconnectedAt > lastMessageAt ? disconnectedAt : lastMessageAt;
        return lastSeen is { } ls && nowUtc - ls <= ReconnectingWindow ? Reconnecting : Offline;
    }
}
