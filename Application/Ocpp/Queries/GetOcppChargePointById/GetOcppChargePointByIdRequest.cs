using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppChargePointById;

/// <summary>Admin: one charger with its connectors, recent sessions and today's totals.</summary>
public record GetOcppChargePointByIdRequest(int Id, int RecentTransactions = 10) : IRequest<OcppChargePointDetailDto>;

public class GetOcppChargePointByIdRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient ocppServer)
    : IRequestHandler<GetOcppChargePointByIdRequest, OcppChargePointDetailDto>
{
    public async Task<OcppChargePointDetailDto> Handle(GetOcppChargePointByIdRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var now = DateTime.UtcNow;

        var cp = await db.OcppChargePoints.AsNoTracking()
            .Include(c => c.ChargingPoint)
            .Include(c => c.Connectors).ThenInclude(k => k.PlugType)
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException($"cannot find OCPP charge point with id: {request.Id}");

        var recent = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.OcppChargePointId == cp.Id)
            .OrderByDescending(t => t.StartedAt)
            .Take(Math.Clamp(request.RecentTransactions, 1, 100))
            .ToListAsync(cancellationToken);

        // "Today" is the station's day (Jordan), expressed in UTC for the query.
        var todayStartUtc = JordanTime.ToUtc(JordanTime.FromUtc(now).Date);
        var sessionsToday = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.OcppChargePointId == cp.Id && t.StartedAt >= todayStartUtc && !t.WasRejected && !t.IsOrphan)
            .ToListAsync(cancellationToken);
        var faultsToday = await db.OcppRawMessages.AsNoTracking()
            .CountAsync(m => m.ChargePointId == cp.ChargePointId && m.Direction == "in" && m.Action == "StatusNotification"
                             && m.CreatedAt >= todayStartUtc && m.Payload != null && m.Payload.Contains("\"Faulted\""), cancellationToken);

        var subscription = await OcppSubscriptionGate.GetStateAsync(db, cp.ChargingPointId, cancellationToken);
        var cardsAtStation = await db.OcppAuthorizedTags.AsNoTracking()
            .CountAsync(t => t.ChargingPointId == cp.ChargingPointId && t.IsEnabled && !t.IsDeleted, cancellationToken);
        var onboarding = await OcppOnboarding.ComputeAsync(db, cp.ChargePointId, cp.LastBootAt, cp.CreatedAt, cancellationToken);

        return new OcppChargePointDetailDto(
            cp.Id, cp.ChargePointId, cp.DisplayName, cp.ChargingPointId, cp.ChargingPoint.Name,
            cp.Vendor, cp.Model, cp.FirmwareVersion, cp.SerialNumber, cp.ChargeBoxSerialNumber, cp.Iccid, cp.Imsi, cp.MeterSerialNumber,
            cp.HeartbeatInterval, cp.IsEnabled, cp.IsConnected,
            OcppLiveness.StateOf(cp.IsConnected, cp.LastMessageAt, cp.DisconnectedAt, cp.HeartbeatInterval, now),
            cp.ConnectedAt, cp.DisconnectedAt, cp.LastBootAt, cp.LastMessageAt, cp.LastRemoteIp,
            cp.PasswordHash != null, cp.FailedAuthCount, cp.LockedUntil,
            $"/ocpp16/{cp.ChargePointId}",
            OcppEndpoint.WebSocketBaseUrl(ocppServer.ServerUrl), OcppEndpoint.Port(ocppServer.ServerUrl),
            subscription,
            cp.Connectors.OrderBy(k => k.ConnectorId).Select(k => OcppConnectorDto.From(k, k.PlugType?.Name)).ToList(),
            recent.Select(t => OcppTransactionDto.From(t, now)).ToList(),
            new OcppChargePointTodayDto(
                sessionsToday.Count,
                sessionsToday.Sum(t => t.EnergyKwh ?? 0m),
                faultsToday),
            new OcppLocalListStateDto(cp.LocalListStatus, cp.LocalListVersion, cp.LocalListSyncedAt, cardsAtStation, cp.LocalListStatus == Cable.Core.Constants.OcppLocalListStatus.Synced),
            onboarding,
            cp.CreatedAt, cp.ModifiedAt);
    }
}
