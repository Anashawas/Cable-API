using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Constants;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppFleetHealth;

/// <summary>Admin dashboard tile for Cable Connect.</summary>
public record GetOcppFleetHealthRequest : IRequest<OcppFleetHealthDto>;

public class GetOcppFleetHealthRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppFleetHealthRequest, OcppFleetHealthDto>
{
    public async Task<OcppFleetHealthDto> Handle(GetOcppFleetHealthRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var now = DateTime.UtcNow;

        var chargers = await db.OcppChargePoints.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .Select(c => new { c.ChargingPointId, c.IsEnabled, c.IsConnected, c.LastMessageAt, c.DisconnectedAt, c.HeartbeatInterval, c.LastBootAt, c.LockedUntil })
            .ToListAsync(cancellationToken);

        var states = chargers.Select(c => OcppLiveness.StateOf(c.IsConnected, c.LastMessageAt, c.DisconnectedAt, c.HeartbeatInterval, now)).ToList();

        var connectors = await db.OcppConnectors.AsNoTracking()
            .Where(k => k.ConnectorId > 0 && !k.ChargePoint.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Free = g.Count(k => k.Status == OcppConnectorStatus.Available),
                Faulted = g.Count(k => k.Status == OcppConnectorStatus.Faulted),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var todayStartUtc = JordanTime.ToUtc(JordanTime.FromUtc(now).Date);
        var sessions = await db.OcppTransactions.AsNoTracking()
            .Where(t => !t.ChargePoint.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Open = g.Count(t => t.IsOpen),
                Stale = g.Count(t => t.IsOpen && t.IsStale),
                Today = g.Count(t => t.StartedAt >= todayStartUtc && !t.WasRejected && !t.IsOrphan),
                EnergyToday = g.Where(t => t.StartedAt >= todayStartUtc && !t.WasRejected && !t.IsOrphan).Sum(t => t.EnergyKwh) ?? 0m,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var stationIds = chargers.Select(c => c.ChargingPointId).Distinct().ToList();
        var subs = await OcppSubscriptionGate.GetStatesAsync(db, stationIds, cancellationToken);

        var openAlerts = await db.OcppAlerts.AsNoTracking()
            .CountAsync(a => a.ResolvedAt == null && !a.IsDeleted && !a.ChargePoint.IsDeleted, cancellationToken);

        return new OcppFleetHealthDto(
            TotalChargers: chargers.Count,
            Enabled: chargers.Count(c => c.IsEnabled),
            Online: states.Count(s => s == OcppLiveness.Online),
            Reconnecting: states.Count(s => s == OcppLiveness.Reconnecting),
            Offline: states.Count(s => s == OcppLiveness.Offline),
            NeverConnected: chargers.Count(c => c.LastBootAt == null),
            Locked: chargers.Count(c => c.LockedUntil > now),
            TotalConnectors: connectors?.Total ?? 0,
            FreeConnectors: connectors?.Free ?? 0,
            FaultedConnectors: connectors?.Faulted ?? 0,
            OpenSessions: sessions?.Open ?? 0,
            StaleSessions: sessions?.Stale ?? 0,
            SessionsToday: sessions?.Today ?? 0,
            EnergyTodayKwh: sessions?.EnergyToday ?? 0m,
            StationsWithChargers: stationIds.Count,
            StationsWithoutActiveSubscription: stationIds.Count(id => !subs[id].IsOn),
            OpenAlerts: openAlerts);
    }
}
