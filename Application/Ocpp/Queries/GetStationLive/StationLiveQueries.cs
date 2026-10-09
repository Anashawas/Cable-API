using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetStationLive;

// ---------------------------------------------------------------------------
// N-3 / N-4 / N-7 — the station as a driver sees it.
//   One answer per station (all cabinets aggregated), free counts per plug type
//   (a free CHAdeMO is no use to a CCS2 car), faults shown as "out of order",
//   driver-facing names per cabinet, and nothing at all unless the N-2 gates are
//   open and the data is fresh.
// ---------------------------------------------------------------------------

/// <summary>Driver-facing plug state. Deliberately coarse: Free | Busy | OutOfOrder | Unknown.</summary>
public static class PlugLiveState
{
    public const string Free = "Free";
    public const string Busy = "Busy";
    public const string OutOfOrder = "OutOfOrder";
    public const string Unknown = "Unknown";

    public static string Of(string? ocppStatus, bool chargerOnline)
    {
        if (!chargerOnline) return Unknown;
        return ocppStatus switch
        {
            OcppConnectorStatus.Available => Free,
            OcppConnectorStatus.Preparing or OcppConnectorStatus.Charging or OcppConnectorStatus.SuspendedEV
                or OcppConnectorStatus.SuspendedEVSE or OcppConnectorStatus.Finishing or OcppConnectorStatus.Reserved => Busy,
            OcppConnectorStatus.Faulted or OcppConnectorStatus.Unavailable => OutOfOrder,
            _ => Unknown,
        };
    }
}

/// <summary>Why a station shows nothing live: NoSubscription | NotShared | Blocked | NoChargers | Offline.</summary>
public static class LiveUnavailableReason
{
    public const string NoSubscription = "NoSubscription";
    public const string NotShared = "NotShared";
    public const string Blocked = "Blocked";
    public const string NoChargers = "NoChargers";
    public const string Offline = "Offline";
}

/// <param name="OcppStatus">The raw OCPP status (Available, Preparing, Charging, Finishing…) — Preparing means a car is connected and waiting, which is when a session may be started.</param>
/// <param name="OpenSessionId">Owner / admin only: the running session on this plug (for Stop); null for drivers.</param>
public record PlugLiveDto(int ConnectorId, string State, int? PlugTypeId, string? PlugTypeName, string? PlugTypeFamily, decimal? PowerKw,
    string? OcppStatus = null, int? OpenSessionId = null);

/// <summary>One cabinet. DisplayName is what the owner named it; null = the app shows its own default ("Charger {Ordinal}"). The OCPP id is never exposed to drivers.</summary>
public record ChargerLiveDto(int Id, int Ordinal, string? DisplayName, bool Online, DateTime? UpdatedAt, List<PlugLiveDto> Plugs);

/// <summary>Per plug type across the whole station — the number the badge shows.</summary>
public record PlugTypeLiveDto(int? PlugTypeId, string Name, string? Family, int Total, int Free, int Busy, int OutOfOrder, int Unknown, decimal? MaxPowerKw);

public record StationLiveDto(
    int ChargingPointId,
    bool Available,
    string? UnavailableReason,
    DateTime? UpdatedAt,
    List<PlugTypeLiveDto> PlugTypes,
    List<ChargerLiveDto> Chargers,
    /// <summary>N-6: true when the station's worst charger scores ≥ the threshold. Drivers get true or null — never a bad number.</summary>
    bool? Reliable = null,
    /// <summary>N-6: the station score (lowest charger), owner / admin only; null for drivers.</summary>
    decimal? ReliabilityPct = null);

/// <summary>Lightweight form for the map / list badge.</summary>
public record PlugTypeFreeDto(int? PlugTypeId, string Name, int Free, int Total);
public record StationLiveSummaryDto(int ChargingPointId, bool Available, string? UnavailableReason, DateTime? UpdatedAt, List<PlugTypeFreeDto> PlugTypes);

internal static class StationLiveBuilder
{
    private const string OtherPlugType = "Other";

    /// <param name="enforceGates">true for drivers (N-2 gates); false for the owner looking at their own station.</param>
    public static async Task<StationLiveDto> BuildAsync(IApplicationDbContext db, int chargingPointId, bool enforceGates, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (enforceGates)
        {
            var v = await OcppLiveVisibility.GetAsync(db, chargingPointId, ct);
            if (!v.VisibleToDrivers)
            {
                var reason = !v.SubscriptionOn ? LiveUnavailableReason.NoSubscription
                           : v.AdminBlocked ? LiveUnavailableReason.Blocked
                           : LiveUnavailableReason.NotShared;
                return new StationLiveDto(chargingPointId, false, reason, null, [], []);
            }
        }

        var chargers = await db.OcppChargePoints.AsNoTracking()
            .Where(c => c.ChargingPointId == chargingPointId && !c.IsDeleted && c.IsEnabled)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id, c.DisplayName, c.IsConnected, c.LastMessageAt, c.DisconnectedAt, c.HeartbeatInterval, c.ReliabilityPct,
                Plugs = c.Connectors.Where(k => k.ConnectorId > 0).OrderBy(k => k.ConnectorId)
                    .Select(k => new
                    {
                        k.ConnectorId, k.Status, k.PlugTypeId, PlugTypeName = k.PlugType != null ? k.PlugType.Name : null, PlugTypeFamily = k.PlugType != null ? k.PlugType.PlugTypeFamily : null, k.PowerKw,
                        OpenSessionId = db.OcppTransactions.Where(t => t.OcppChargePointId == c.Id && t.ConnectorId == k.ConnectorId && t.IsOpen && !t.WasRejected && !t.IsStale)
                            .OrderByDescending(t => t.Id).Select(t => (int?)t.Id).FirstOrDefault(),
                    })
                    .ToList(),
            })
            .ToListAsync(ct);

        if (chargers.Count == 0)
            return new StationLiveDto(chargingPointId, false, LiveUnavailableReason.NoChargers, null, [], []);

        var chargerDtos = new List<ChargerLiveDto>();
        var ordinal = 0;
        foreach (var c in chargers)
        {
            ordinal++;
            var online = OcppLiveness.StateOf(c.IsConnected, c.LastMessageAt, c.DisconnectedAt, c.HeartbeatInterval, now) == OcppLiveness.Online;
            var plugs = c.Plugs.Select(p => new PlugLiveDto(p.ConnectorId, PlugLiveState.Of(p.Status, online), p.PlugTypeId, p.PlugTypeName, p.PlugTypeFamily, p.PowerKw,
                online ? p.Status : null, enforceGates ? null : p.OpenSessionId)).ToList();
            chargerDtos.Add(new ChargerLiveDto(c.Id, ordinal, string.IsNullOrWhiteSpace(c.DisplayName) ? null : c.DisplayName, online, c.LastMessageAt, plugs));
        }

        var anyOnline = chargerDtos.Any(c => c.Online);
        var plugTypes = chargerDtos.SelectMany(c => c.Plugs)
            .GroupBy(p => p.PlugTypeId)
            .Select(g => new PlugTypeLiveDto(
                g.Key,
                g.First().PlugTypeName ?? OtherPlugType,
                g.First().PlugTypeFamily,
                g.Count(),
                g.Count(p => p.State == PlugLiveState.Free),
                g.Count(p => p.State == PlugLiveState.Busy),
                g.Count(p => p.State == PlugLiveState.OutOfOrder),
                g.Count(p => p.State == PlugLiveState.Unknown),
                g.Max(p => p.PowerKw)))
            .OrderByDescending(t => t.Total).ThenBy(t => t.Name)
            .ToList();

        var updatedAt = chargerDtos.Where(c => c.Online).Select(c => c.UpdatedAt).Max();

        // N-6: the station is as reliable as its worst cabinet. Drivers see only "reliable" (true) or nothing.
        var scores = chargers.Where(c => c.ReliabilityPct != null).Select(c => c.ReliabilityPct!.Value).ToList();
        decimal? stationPct = scores.Count == 0 ? null : scores.Min();
        bool? reliable = stationPct is null ? null : stationPct >= OcppLimits.ReliableThresholdPct ? true : (enforceGates ? null : false);

        return new StationLiveDto(chargingPointId, anyOnline, anyOnline ? null : LiveUnavailableReason.Offline, updatedAt, plugTypes, chargerDtos,
            reliable, enforceGates ? null : stationPct);
    }

    public static StationLiveSummaryDto Summarize(StationLiveDto live) => new(
        live.ChargingPointId, live.Available, live.UnavailableReason, live.UpdatedAt,
        live.PlugTypes.Select(t => new PlugTypeFreeDto(t.PlugTypeId, t.Name, t.Free, t.Total)).ToList());
}

// ---------------------------------------------------------------------------
// Driver app
// ---------------------------------------------------------------------------

/// <summary>B2C: the live picture of one station. Empty with a reason unless the N-2 gates are open.</summary>
public record GetStationLiveRequest(int ChargingPointId) : IRequest<StationLiveDto>;

public class GetStationLiveRequestHandler(IApplicationDbContext db) : IRequestHandler<GetStationLiveRequest, StationLiveDto>
{
    public async Task<StationLiveDto> Handle(GetStationLiveRequest request, CancellationToken cancellationToken)
    {
        var exists = await db.ChargingPoints.AsNoTracking().AnyAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken);
        if (!exists) throw new NotFoundException("cannot find charging point with id: " + request.ChargingPointId);
        return await StationLiveBuilder.BuildAsync(db, request.ChargingPointId, enforceGates: true, cancellationToken);
    }
}

/// <summary>B2C: badge data for the stations on screen ("CCS2 1 free · CHAdeMO 0 free"). Up to 50 ids per call.</summary>
public record GetStationsLiveSummaryRequest(List<int> ChargingPointIds) : IRequest<List<StationLiveSummaryDto>>;

public class GetStationsLiveSummaryRequestValidator : AbstractValidator<GetStationsLiveSummaryRequest>
{
    public GetStationsLiveSummaryRequestValidator()
    {
        RuleFor(x => x.ChargingPointIds).NotEmpty().Must(ids => ids.Count <= 50).WithMessage("At most 50 station ids per call");
    }
}

public class GetStationsLiveSummaryRequestHandler(IApplicationDbContext db) : IRequestHandler<GetStationsLiveSummaryRequest, List<StationLiveSummaryDto>>
{
    public async Task<List<StationLiveSummaryDto>> Handle(GetStationsLiveSummaryRequest request, CancellationToken cancellationToken)
    {
        // Only stations that have chargers at all; the rest simply get no entry (the app shows nothing).
        var ids = request.ChargingPointIds.Distinct().ToList();
        var withChargers = await db.OcppChargePoints.AsNoTracking()
            .Where(c => ids.Contains(c.ChargingPointId) && !c.IsDeleted && c.IsEnabled)
            .Select(c => c.ChargingPointId).Distinct().ToListAsync(cancellationToken);

        var result = new List<StationLiveSummaryDto>();
        foreach (var id in withChargers)
            result.Add(StationLiveBuilder.Summarize(await StationLiveBuilder.BuildAsync(db, id, enforceGates: true, cancellationToken)));
        return result;
    }
}

// ---------------------------------------------------------------------------
// Partner app (owner / managers)
// ---------------------------------------------------------------------------

/// <summary>Owner / manager: the same live picture of their own station, gates not applied (it is their data).</summary>
public record GetMyStationLiveRequest(int ChargingPointId) : IRequest<StationLiveDto>;

public class GetMyStationLiveRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetMyStationLiveRequest, StationLiveDto>
{
    public async Task<StationLiveDto> Handle(GetMyStationLiveRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken);
        return await StationLiveBuilder.BuildAsync(db, request.ChargingPointId, enforceGates: false, cancellationToken);
    }
}

/// <summary>N-3: the owner names a cabinet the way a driver would recognise it ("the right-hand charger", "next to the café").</summary>
public record SetChargerDisplayNameCommand(int ChargingPointId, int OcppChargePointId, string? DisplayName) : IRequest;

public class SetChargerDisplayNameCommandValidator : AbstractValidator<SetChargerDisplayNameCommand>
{
    public SetChargerDisplayNameCommandValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(100);
    }
}

public class SetChargerDisplayNameCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SetChargerDisplayNameCommand>
{
    public async Task Handle(SetChargerDisplayNameCommand request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken);
        var cp = await db.OcppChargePoints.FirstOrDefaultAsync(c => c.Id == request.OcppChargePointId && c.ChargingPointId == request.ChargingPointId && !c.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException("cannot find charger " + request.OcppChargePointId + " at this station");
        cp.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        await db.SaveChanges(cancellationToken);
    }
}
