using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Ocpp.Commands.ControlOcppChargePoint;
using Application.Ocpp.Queries;
using Application.Ocpp.Queries.GetSessionLive;
using Application.Pricing;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.DriverSessions;

// ---------------------------------------------------------------------------
// Phase 3 — start and stop a session from the driver app (no card needed).
//
// The app asks for a plug; we send RemoteStartTransaction with the driver's virtual
// tag (CBL-U{userId}). The charger authorizes that tag against our own request
// (TagAuthorizer), opens the session and reports it like any card session, so the
// driver's history, the price and the parked-car alert all work the same way.
// ---------------------------------------------------------------------------

/// <summary>Pre-flight shared by the driver, partner and admin remote start.</summary>
public static class OcppRemoteStart
{
    public sealed record Target(OcppChargePoint ChargePoint, OcppConnector Connector);

    /// <summary>The charger must be enabled and online, the plug must have a car connected (Preparing, no error) and no open session. A RemoteStart into an empty plug is refused: the unit would wait 60 s and cancel.</summary>
    public static async Task<Target> EnsureCanStartAsync(IApplicationDbContext db, int ocppChargePointId, int connectorId, CancellationToken ct)
    {
        var cp = await db.OcppChargePoints.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ocppChargePointId && !c.IsDeleted, ct)
                 ?? throw new NotFoundException("cannot find charger " + ocppChargePointId);
        if (!cp.IsEnabled) throw new DataValidationException("ChargerId", "This charger is disabled.");
        if (!cp.IsConnected) throw new DataValidationException("ChargerId", "This charger is offline right now. Try again in a minute.");

        var k = await db.OcppConnectors.AsNoTracking().FirstOrDefaultAsync(x => x.OcppChargePointId == cp.Id && x.ConnectorId == connectorId, ct)
                ?? throw new NotFoundException("cannot find plug " + connectorId + " on this charger");
        if (connectorId < 1) throw new DataValidationException("ConnectorId", "Pick a plug (connectorId 1 or more).");
        if (k.ErrorCode != "NoError" || k.Status is OcppConnectorStatus.Faulted or OcppConnectorStatus.Unavailable)
            throw new DataValidationException("ConnectorId", "This plug is out of order.");
        if (k.Status == OcppConnectorStatus.Available)
            throw new DataValidationException("ConnectorId", "Plug the cable into the car first. The plug shows 'Preparing' once the car is connected.");
        if (k.Status != OcppConnectorStatus.Preparing)
            throw new DataValidationException("ConnectorId", "This plug is busy (" + k.Status + ").");

        var open = await db.OcppTransactions.AsNoTracking()
            .AnyAsync(t => t.OcppChargePointId == cp.Id && t.ConnectorId == connectorId && t.IsOpen && !t.WasRejected, ct);
        if (open) throw new DataValidationException("ConnectorId", "A session is already running on this plug.");

        return new Target(cp, k);
    }

    public static Dictionary<string, object> Payload(int connectorId, string idTag) =>
        new() { ["connectorId"] = connectorId, ["idTag"] = idTag };

    /// <summary>
    /// A charge limit chosen with the start ("stop at 7 JOD"): parked on the plug until the session this start
    /// opens takes it (StartTransaction, within OcppVirtualTag.RemoteStartAuthorizeWindow). No kind = nothing parked.
    /// </summary>
    public static async Task SetPendingLimitAsync(IApplicationDbContext db, int ocppChargePointId, int connectorId,
        string? kind, decimal? value, int? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind)) return;
        var (k, v) = OcppChargeLimit.Validate(kind, value);
        var plug = await db.OcppConnectors.FirstAsync(x => x.OcppChargePointId == ocppChargePointId && x.ConnectorId == connectorId, ct);
        plug.PendingLimitKind = k;
        plug.PendingLimitValue = v;
        plug.PendingLimitSetBy = userId;
        plug.PendingLimitSetAt = DateTime.UtcNow;
        await db.SaveChanges(ct);
    }
}

public record OcppSessionStartDto(
    int CommandId,
    bool Accepted,
    string Status,
    string? ResultStatus,
    string IdTag,
    /// <summary>What the app should tell the driver.</summary>
    string Message);

/// <summary>Driver: start charging on a plug of a charger at a station that shares its live status.</summary>
/// <param name="ChargeLimitKind">Optional: stop automatically at Jod | Kwh | Soc = ChargeLimitValue.</param>
public record StartMyOcppSessionCommand(int ChargingPointId, int ChargerId, int ConnectorId, string? ChargeLimitKind = null, decimal? ChargeLimitValue = null) : IRequest<OcppSessionStartDto>
{
    /// <summary>Optional §11.d: the plug's CHG- sticker code (or its link) when the start came from a scan; it must be this plug's code. The session is then recorded with startMethod Sticker.</summary>
    public string? ConnectorCode { get; init; }
}

public class StartMyOcppSessionCommandValidator : AbstractValidator<StartMyOcppSessionCommand>
{
    public StartMyOcppSessionCommandValidator()
    {
        RuleFor(x => x.ChargingPointId).GreaterThan(0);
        RuleFor(x => x.ChargerId).GreaterThan(0);
        RuleFor(x => x.ConnectorId).GreaterThanOrEqualTo(1);
    }
}

public class StartMyOcppSessionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<StartMyOcppSessionCommand, OcppSessionStartDto>
{
    public async Task<OcppSessionStartDto> Handle(StartMyOcppSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        // The station must be open to drivers (subscription on, owner sharing, no admin veto).
        var visibility = await OcppLiveVisibility.GetAsync(db, request.ChargingPointId, cancellationToken);
        if (!visibility.VisibleToDrivers)
            throw new DataValidationException("ChargingPointId", "This station does not offer app charging.");

        // A scanned sticker must be this plug's (404 CODE_NOT_FOUND · 410 CODE_REVOKED · 422 CODE_MISMATCH).
        if (!string.IsNullOrWhiteSpace(request.ConnectorCode))
            await Application.Ocpp.LiveCharging.ConnectorCodeService.EnsureMatchesAsync(db, request.ConnectorCode, request.ChargerId, request.ConnectorId, cancellationToken);

        var target = await OcppRemoteStart.EnsureCanStartAsync(db, request.ChargerId, request.ConnectorId, cancellationToken);
        if (target.ChargePoint.ChargingPointId != request.ChargingPointId)
            throw new NotFoundException("cannot find charger " + request.ChargerId + " at this station");

        var mine = await db.OcppTransactions.AsNoTracking()
            .CountAsync(t => t.StartedByUserId == userId && t.IsOpen && !t.WasRejected, cancellationToken);
        if (mine >= OcppLimits.OpenSessionsPerDriver)
            throw new ConflictException("sessionAlreadyOpen",
                System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar"
                    ? "عندك شحن شغّال. أوقفه قبل ما تبدأ شحن ثاني."
                    : "You already have a running session. Stop it before starting another.");

        var idTag = await EnsureUserTagAsync(db, userId, cancellationToken);
        await OcppRemoteStart.SetPendingLimitAsync(db, target.ChargePoint.Id, request.ConnectorId, request.ChargeLimitKind, request.ChargeLimitValue, userId, cancellationToken);
        await Application.Ocpp.LiveCharging.StartMethodParking.ParkAsync(db, target.ChargePoint.Id, request.ConnectorId,
            string.IsNullOrWhiteSpace(request.ConnectorCode) ? null : OcppStartMethod.Sticker, cancellationToken);
        var result = await OcppCommandRunner.RunAsync(db, currentUser, client, target.ChargePoint.Id, "RemoteStartTransaction",
            OcppRemoteStart.Payload(request.ConnectorId, idTag), cancellationToken, skipAccessGuard: true);

        var message = result.Accepted
            ? "The charger accepted. Plug in if you have not yet; charging starts in a few seconds."
            : result.Status != "Answered"
                ? "The charger did not answer. Check it is online and try again."
                : "The charger refused to start (" + (result.ResultStatus ?? "Rejected") + "). Try another plug or tap a card.";
        return new OcppSessionStartDto(result.CommandId, result.Accepted, result.Status, result.ResultStatus, idTag, message);
    }

    /// <summary>The driver's virtual tag, created on first use. The same tag links card-less sessions to the user everywhere.</summary>
    internal static async Task<string> EnsureUserTagAsync(IApplicationDbContext db, int userId, CancellationToken ct)
    {
        var idTag = OcppVirtualTag.ForUser(userId);
        var row = await db.OcppUserIdTags.FirstOrDefaultAsync(u => u.IdTag == idTag && !u.IsDeleted, ct);
        if (row is null)
        {
            db.OcppUserIdTags.Add(new OcppUserIdTag { UserId = userId, IdTag = idTag, Label = "Cable app", IsEnabled = true });
            await db.SaveChanges(ct);
        }
        else if (!row.IsEnabled)
        {
            throw new DataValidationException("ChargerId", "App charging is switched off for your account.");
        }
        return idTag;
    }
}

/// <summary>Driver: stop their own running session.</summary>
public record StopMyOcppSessionCommand(int TransactionId) : IRequest<OcppCommandResultDto>;

public class StopMyOcppSessionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<StopMyOcppSessionCommand, OcppCommandResultDto>
{
    public async Task<OcppCommandResultDto> Handle(StopMyOcppSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var tx = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == request.TransactionId && t.StartedByUserId == userId)
            .Select(t => new { t.OcppChargePointId, t.IsOpen })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("cannot find your session " + request.TransactionId);
        if (!tx.IsOpen) throw new DataValidationException(nameof(request.TransactionId), "This session is already finished.");

        return await OcppCommandRunner.RunAsync(db, currentUser, client, tx.OcppChargePointId, "RemoteStopTransaction",
            new Dictionary<string, object> { ["transactionId"] = request.TransactionId }, cancellationToken, skipAccessGuard: true);
    }
}

// ---------------------------------------------------------------------------
// Driver's sessions: the running one (live) and the history (with price)
// ---------------------------------------------------------------------------

public record MySessionDto(
    int Id,
    int ChargingPointId,
    string StationName,
    int ChargerId,
    string? ChargerName,
    int ConnectorId,
    DateTime StartedAt,
    DateTime? StoppedAt,
    int DurationSec,
    decimal? EnergyKwh,
    bool IsOpen,
    string StartSource,
    string? StopReason,
    string? StopReasonText,
    string? StopReasonTextAr,
    int? CostFils,
    decimal? CostJod,
    int? TariffVersion,
    List<SessionPriceLineDto>? Price,
    /// <summary>Latest meter sample while open: power now, battery level, when it was measured.</summary>
    int? PowerW = null,
    int? SocPercent = null,
    DateTime? LastSampleAt = null,
    /// <summary>Battery % at the start of the session and at the latest / final reading.</summary>
    int? SocStartPercent = null,
    int? SocEndPercent = null,
    /// <summary>Latest current (A), voltage (V) and charger temperature (°C, when the unit reports it) while open.</summary>
    decimal? CurrentA = null,
    decimal? VoltageV = null,
    decimal? TemperatureC = null,
    /// <summary>"Stop at …" — null when none.</summary>
    Application.Ocpp.Commands.ChargeLimits.ChargeLimitDto? ChargeLimit = null);

internal static class MySessionProjection
{
    public static IQueryable<MySessionDto> Project(IQueryable<OcppTransaction> q, DateTime now) =>
        q.Select(t => new MySessionDto(
            t.Id, t.ChargePoint.ChargingPointId, t.ChargePoint.ChargingPoint.Name, t.OcppChargePointId, t.ChargePoint.DisplayName, t.ConnectorId,
            t.StartedAt, t.StoppedAt, 0, t.EnergyKwh, t.IsOpen, t.StartSource, t.StopReason, null, null,
            t.CostFils, t.CostFils == null ? null : t.CostFils / 1000m, t.TariffVersion, null,
            null, null, null, t.SocStartPercent, t.SocEndPercent, null, null, null,
            t.ChargeLimitKind == null ? null : new Application.Ocpp.Commands.ChargeLimits.ChargeLimitDto(
                t.ChargeLimitKind, t.ChargeLimitValue, null, null, t.ChargeLimitSetAt, t.ChargeLimitReachedAt)));

    public static MySessionDto Finish(MySessionDto d, DateTime now, string? breakdownJson)
    {
        var limit = d.ChargeLimit;
        var (en, ar) = Application.Ocpp.Commands.ChargeLimits.ChargeLimitDto.StopText(d.StopReason, limit?.Kind, limit?.Value, limit?.ReachedAt);
        return d with
        {
            DurationSec = (int)((d.StoppedAt ?? now) - d.StartedAt).TotalSeconds,
            StopReasonText = en,
            StopReasonTextAr = ar,
            Price = SessionPricingService.ReadLines(breakdownJson),
            ChargeLimit = limit is null ? null : Application.Ocpp.Commands.ChargeLimits.ChargeLimitDto.From(limit.Kind, limit.Value, limit.SetAt, limit.ReachedAt),
        };
    }
}

/// <summary>Driver: the session running now, with the latest sample; null when none.</summary>
public record GetMyCurrentOcppSessionRequest : IRequest<MySessionDto?>;

public class GetMyCurrentOcppSessionRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyCurrentOcppSessionRequest, MySessionDto?>
{
    public async Task<MySessionDto?> Handle(GetMyCurrentOcppSessionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;

        var open = await MySessionProjection.Project(
                db.OcppTransactions.AsNoTracking().Where(t => t.StartedByUserId == userId && t.IsOpen && !t.WasRejected).OrderByDescending(t => t.Id), now)
            .FirstOrDefaultAsync(cancellationToken);
        if (open is null) return null;

        var sample = await db.OcppMeterValues.AsNoTracking()
            .Where(m => m.OcppTransactionId == open.Id)
            .OrderByDescending(m => m.MeasuredAt)
            .Select(m => new { m.PowerW, m.SocPercent, m.MeasuredAt, m.EnergyWh, m.CurrentA, m.VoltageV, m.TemperatureC })
            .FirstOrDefaultAsync(cancellationToken);

        var dto = MySessionProjection.Finish(open, now, null);
        if (sample is null) return dto;

        // Energy so far from the latest register reading (EnergyKwh is only set at stop).
        var start = await db.OcppTransactions.AsNoTracking().Where(t => t.Id == open.Id).Select(t => t.MeterStartWh).FirstAsync(cancellationToken);
        var soFar = sample.EnergyWh is long wh && wh >= start ? (wh - start) / 1000m : dto.EnergyKwh;
        return dto with
        {
            PowerW = sample.PowerW, SocPercent = sample.SocPercent, LastSampleAt = sample.MeasuredAt, EnergyKwh = soFar,
            CurrentA = sample.CurrentA, VoltageV = sample.VoltageV, TemperatureC = sample.TemperatureC,
        };
    }
}

/// <summary>
/// Driver: one of their own sessions in detail — the same view the station owner gets (readings curve, cost so far
/// and price lines while open, battery start → end, limit). Only sessions the driver started (app, or a card linked
/// to their account); anything else is "not found".
/// </summary>
public record GetMyOcppSessionRequest(int TransactionId) : IRequest<SessionLiveDto>;

public class GetMyOcppSessionRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyOcppSessionRequest, SessionLiveDto>
{
    public async Task<SessionLiveDto> Handle(GetMyOcppSessionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var mine = await db.OcppTransactions.AsNoTracking()
            .AnyAsync(t => t.Id == request.TransactionId && t.StartedByUserId == userId && !t.WasRejected && !t.IsOrphan, cancellationToken);
        if (!mine) throw new NotFoundException("cannot find your session " + request.TransactionId);
        return await SessionLiveBuilder.BuildAsync(db, request.TransactionId, null, cancellationToken);
    }
}

/// <summary>Driver: past sessions, newest first, with the price of each.</summary>
public record GetMyOcppSessionsRequest(int? Page = null, int? PageSize = null) : IRequest<PagedResult<MySessionDto>>;

public class GetMyOcppSessionsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyOcppSessionsRequest, PagedResult<MySessionDto>>
{
    public async Task<PagedResult<MySessionDto>> Handle(GetMyOcppSessionsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;

        var page = await MySessionProjection.Project(
                db.OcppTransactions.AsNoTracking().Where(t => t.StartedByUserId == userId && !t.WasRejected && !t.IsOrphan).OrderByDescending(t => t.StartedAt), now)
            .ToPaginatedAsync(request.Page, request.PageSize, 20, 100, cancellationToken);

        var ids = page.Items.Select(i => i.Id).ToList();
        var breakdowns = await db.OcppTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.CostBreakdownJson != null)
            .Select(t => new { t.Id, t.CostBreakdownJson })
            .ToDictionaryAsync(t => t.Id, t => t.CostBreakdownJson, cancellationToken);

        return page.As(page.Items.Select(d => MySessionProjection.Finish(d, now, breakdowns.GetValueOrDefault(d.Id))).ToList());
    }
}
