using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppChargePoints;

/// <summary>Admin: every registered charger with live state, filterable and paged.</summary>
public record GetOcppChargePointsRequest(
    int? ChargingPointId = null,
    string? Search = null,
    bool? IsEnabled = null,
    /// <summary>Online | Reconnecting | Offline (OcppLiveness). Derived, so filtered after the fleet is loaded.</summary>
    string? ConnectionState = null,
    int? Page = null,
    int? PageSize = null) : IRequest<PagedResult<OcppChargePointListItemDto>>;

public class GetOcppChargePointsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppChargePointsRequest, PagedResult<OcppChargePointListItemDto>>
{
    /// <summary>What the list needs from each charger row, before liveness and subscription are added.</summary>
    private sealed record Row(
        int Id, string ChargePointId, string? DisplayName, int ChargingPointId, string StationName,
        string? Vendor, string? Model, string? FirmwareVersion,
        bool IsEnabled, bool IsConnected, DateTime? ConnectedAt, DateTime? DisconnectedAt, DateTime? LastMessageAt, DateTime? LastBootAt,
        bool HasPassword, DateTime? LockedUntil, int HeartbeatInterval, DateTime CreatedAt,
        int ConnectorCount, int FreeConnectors, int FaultedConnectors, int OpenSessions, decimal? ReliabilityPct);

    public async Task<PagedResult<OcppChargePointListItemDto>> Handle(GetOcppChargePointsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var now = DateTime.UtcNow;

        var query = db.OcppChargePoints.AsNoTracking().Where(c => !c.IsDeleted);

        if (request.ChargingPointId is int stationId)
            query = query.Where(c => c.ChargingPointId == stationId);
        if (request.IsEnabled is bool enabled)
            query = query.Where(c => c.IsEnabled == enabled);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            query = query.Where(c => c.ChargePointId.Contains(s)
                                     || (c.DisplayName != null && c.DisplayName.Contains(s))
                                     || c.ChargingPoint.Name.Contains(s)
                                     || (c.SerialNumber != null && c.SerialNumber.Contains(s))
                                     || (c.Vendor != null && c.Vendor.Contains(s))
                                     || (c.Model != null && c.Model.Contains(s)));
        }

        var projected = query
            .OrderByDescending(c => c.IsConnected)
            .ThenBy(c => c.ChargingPoint.Name)
            .ThenBy(c => c.ChargePointId)
            .Select(c => new Row(
                c.Id, c.ChargePointId, c.DisplayName, c.ChargingPointId, c.ChargingPoint.Name,
                c.Vendor, c.Model, c.FirmwareVersion,
                c.IsEnabled, c.IsConnected, c.ConnectedAt, c.DisconnectedAt, c.LastMessageAt, c.LastBootAt,
                c.PasswordHash != null, c.LockedUntil, c.HeartbeatInterval, c.CreatedAt,
                c.Connectors.Count(k => k.ConnectorId > 0),
                c.Connectors.Count(k => k.ConnectorId > 0 && k.Status == OcppConnectorStatus.Available),
                c.Connectors.Count(k => k.Status == OcppConnectorStatus.Faulted),
                c.Transactions.Count(t => t.IsOpen),
                c.ReliabilityPct));

        var wantState = string.IsNullOrWhiteSpace(request.ConnectionState) ? null : request.ConnectionState.Trim();
        if (wantState is null)
        {
            var page = await projected.ToPaginatedAsync(request.Page, request.PageSize, 20, 200, cancellationToken);
            var subs = await OcppSubscriptionGate.GetStatesAsync(db, page.Items.Select(x => x.ChargingPointId), cancellationToken);
            return page.As(await WithOnboardingAsync(page.Items.Select(x => ToDto(x, subs, now)).ToList(), cancellationToken));
        }

        // Connection state is derived from freshness, not stored: load the (small) fleet and page in memory.
        var all = await projected.ToListAsync(cancellationToken);
        var subsAll = await OcppSubscriptionGate.GetStatesAsync(db, all.Select(x => x.ChargingPointId), cancellationToken);
        var filtered = await WithOnboardingAsync(all.Select(x => ToDto(x, subsAll, now))
            .Where(d => string.Equals(d.ConnectionState, wantState, StringComparison.OrdinalIgnoreCase))
            .ToList(), cancellationToken);
        var p = Math.Max(1, request.Page ?? 1);
        var size = Math.Clamp(request.PageSize ?? 20, 1, 200);
        return new PagedResult<OcppChargePointListItemDto>(filtered.Skip((p - 1) * size).Take(size).ToList(), filtered.Count, p, size);
    }

    /// <summary>Never-booted chargers get their onboarding state (Waiting / Connected / Refused) from the raw log — a handful of rows at most.</summary>
    private async Task<List<OcppChargePointListItemDto>> WithOnboardingAsync(List<OcppChargePointListItemDto> items, CancellationToken ct)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].LastBootAt is not null) continue;
            var ob = await OcppOnboarding.ComputeAsync(db, items[i].ChargePointId, null, items[i].CreatedAt, ct);
            items[i] = items[i] with { OnboardingState = ob.State, OnboardingReason = ob.Reason };
        }
        return items;
    }

    private static OcppChargePointListItemDto ToDto(Row x, Dictionary<int, OcppSubscriptionStateDto> subs, DateTime now) => new(
        x.Id, x.ChargePointId, x.DisplayName, x.ChargingPointId, x.StationName,
        x.Vendor, x.Model, x.FirmwareVersion,
        x.IsEnabled, x.IsConnected,
        OcppLiveness.StateOf(x.IsConnected, x.LastMessageAt, x.DisconnectedAt, x.HeartbeatInterval, now),
        x.ConnectedAt, x.DisconnectedAt, x.LastMessageAt, x.LastBootAt,
        x.HasPassword, x.LockedUntil,
        x.ConnectorCount, x.FreeConnectors, x.FaultedConnectors, x.OpenSessions,
        subs[x.ChargingPointId],
        x.CreatedAt,
        ReliabilityPct: x.ReliabilityPct);
}
