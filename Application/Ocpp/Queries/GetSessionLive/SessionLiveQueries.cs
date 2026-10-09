using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Pricing;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetSessionLive;

// ---------------------------------------------------------------------------
// One session in detail — live while it runs (power now, energy so far, battery,
// cost so far at the current tariff rate) and as a record once it is closed —
// plus the sampled curve for a chart. Owner / manager for their station, admin
// for any. Polled every few seconds by the partner web while a plug is busy.
// ---------------------------------------------------------------------------

public record SessionSampleDto(DateTime At, int? PowerW, int? SocPercent, decimal? EnergyKwh);

public record SessionLiveDto(
    int Id,
    int ChargingPointId,
    int ChargerId,
    string? ChargerName,
    int ConnectorId,
    DateTime StartedAt,
    DateTime? StoppedAt,
    bool IsOpen,
    int DurationSec,
    string StartSource,
    string? StopReason,
    string? StopReasonText,
    string? StopReasonTextAr,
    /// <summary>Energy so far (latest register reading) while open; the final figure once closed.</summary>
    decimal? EnergyKwh,
    int? PowerW,
    int? SocPercent,
    decimal? VoltageV,
    decimal? CurrentA,
    DateTime? LastSampleAt,
    int? SecondsSinceSample,
    int? MaxPowerW,
    int? AvgPowerW,
    /// <summary>Closed: the stored price. Open: the price so far, recomputed from the samples at the tariff in force.</summary>
    int? CostFils,
    decimal? CostJod,
    /// <summary>Open only: the fils/kWh of the tariff window right now and its name.</summary>
    int? CurrentRateFils,
    string? CurrentWindowNameEn,
    string? CurrentWindowNameAr,
    List<SessionPriceLineDto>? Price,
    /// <summary>Up to 120 points, oldest first.</summary>
    List<SessionSampleDto> Series);

internal static class SessionLiveBuilder
{
    private const int MaxPoints = 120;

    public static async Task<SessionLiveDto> BuildAsync(IApplicationDbContext db, int transactionId, int? chargingPointId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tx = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == transactionId && (chargingPointId == null || t.ChargePoint.ChargingPointId == chargingPointId))
            .Select(t => new
            {
                t.Id, t.OcppChargePointId, t.ChargePoint.ChargingPointId, t.ChargePoint.DisplayName, t.ConnectorId, t.StartedAt, t.StoppedAt, t.IsOpen,
                t.StartSource, t.StopReason, t.MeterStartWh, t.MeterStopWh, t.EnergyKwh, t.CostFils, t.CostBreakdownJson, t.WasRejected, t.IsOrphan,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("cannot find session " + transactionId);

        var samples = await db.OcppMeterValues.AsNoTracking()
            .Where(m => m.OcppTransactionId == tx.Id)
            .OrderBy(m => m.MeasuredAt)
            .Select(m => new { m.MeasuredAt, m.PowerW, m.SocPercent, m.EnergyWh, m.VoltageV, m.CurrentA })
            .ToListAsync(ct);

        var last = samples.LastOrDefault();
        var powered = samples.Where(s => s.PowerW is > 0).Select(s => s.PowerW!.Value).ToList();

        // Energy: the final figure once closed, else the latest register reading above the start.
        decimal? energy = tx.EnergyKwh;
        if (tx.IsOpen)
        {
            var latestWh = samples.Where(s => s.EnergyWh != null && s.EnergyWh >= tx.MeterStartWh).Select(s => s.EnergyWh).LastOrDefault();
            energy = latestWh is long wh ? (wh - tx.MeterStartWh) / 1000m : 0m;
        }

        // Cost: stored when closed; "so far" at the tariff in force when open.
        int? cost = tx.CostFils;
        List<SessionPriceLineDto>? lines = SessionPricingService.ReadLines(tx.CostBreakdownJson);
        int? rate = null; string? windowEn = null, windowAr = null;
        if (tx.IsOpen && !tx.WasRejected)
        {
            var tariff = await TouTariffLoader.LoadAsync(db, ct);
            if (tariff is not null)
            {
                var latestWh = samples.Where(s => s.EnergyWh != null).Select(s => s.EnergyWh).LastOrDefault() ?? tx.MeterStartWh;
                var soFar = SessionPricer.Compute(tariff, tx.StartedAt, now, tx.MeterStartWh, Math.Max(latestWh, tx.MeterStartWh),
                    samples.Where(s => s.EnergyWh != null).Select(s => (s.MeasuredAt, s.EnergyWh!.Value)));
                cost = soFar.CostFils;
                lines = soFar.Lines;
                var w = SessionPricer.WindowNow(tariff, now);
                rate = w?.PriceFils; windowEn = w?.NameEn; windowAr = w?.NameAr;
            }
        }

        // Downsample evenly to MaxPoints, always keeping the last one.
        var series = samples.Select(s => new SessionSampleDto(s.MeasuredAt, s.PowerW, s.SocPercent,
                s.EnergyWh is long e && e >= tx.MeterStartWh ? (e - tx.MeterStartWh) / 1000m : null)).ToList();
        if (series.Count > MaxPoints)
        {
            var step = (double)series.Count / MaxPoints;
            var picked = Enumerable.Range(0, MaxPoints).Select(i => series[(int)(i * step)]).ToList();
            if (picked[^1] != series[^1]) picked[^1] = series[^1];
            series = picked;
        }

        return new SessionLiveDto(
            tx.Id, tx.ChargingPointId, tx.OcppChargePointId, tx.DisplayName, tx.ConnectorId, tx.StartedAt, tx.StoppedAt, tx.IsOpen,
            (int)((tx.StoppedAt ?? now) - tx.StartedAt).TotalSeconds, tx.StartSource, tx.StopReason,
            OcppStopReason.Describe(tx.StopReason), OcppStopReason.DescribeAr(tx.StopReason),
            energy, last?.PowerW, last?.SocPercent, last?.VoltageV, last?.CurrentA, last?.MeasuredAt,
            last is null ? null : (int)(now - last.MeasuredAt).TotalSeconds,
            powered.Count == 0 ? null : powered.Max(), powered.Count == 0 ? null : (int)powered.Average(),
            cost, cost is int c ? c / 1000m : null, rate, windowEn, windowAr, lines, series);
    }
}

/// <summary>Owner / manager: one of their station's sessions in detail.</summary>
public record GetMySessionLiveRequest(int ChargingPointId, int TransactionId) : IRequest<SessionLiveDto>;

public class GetMySessionLiveRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetMySessionLiveRequest, SessionLiveDto>
{
    public async Task<SessionLiveDto> Handle(GetMySessionLiveRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectView);
        return await SessionLiveBuilder.BuildAsync(db, request.TransactionId, request.ChargingPointId, cancellationToken);
    }
}

/// <summary>Admin: any session in detail.</summary>
public record GetOcppSessionLiveRequest(int TransactionId) : IRequest<SessionLiveDto>;

public class GetOcppSessionLiveRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetOcppSessionLiveRequest, SessionLiveDto>
{
    public async Task<SessionLiveDto> Handle(GetOcppSessionLiveRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await SessionLiveBuilder.BuildAsync(db, request.TransactionId, null, cancellationToken);
    }
}
