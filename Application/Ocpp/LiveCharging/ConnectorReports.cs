using Application.Common.Security;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §13.3 — "the plug didn't work": a one-tap report from the driver, the owner's list and "fixed",
// the admin's view grouped per plug, and the "may have a problem" flag in /live.
// ---------------------------------------------------------------------------

/// <summary>Pure rule for the "may have a problem" warning (§8 decision 14).</summary>
public static class SuspectedIssueRule
{
    /// <summary>
    /// True when at least <see cref="LiveChargingLimits.SuspectReports"/> of the open reports, all newer than the
    /// plug's last successful session start, fall within <see cref="LiveChargingLimits.SuspectWindow"/> of each other.
    /// </summary>
    public static bool IsSuspected(IEnumerable<DateTime> openReportTimes, DateTime? lastSuccessfulStart)
    {
        var times = openReportTimes.Where(t => lastSuccessfulStart is null || t > lastSuccessfulStart).OrderBy(t => t).ToList();
        var n = LiveChargingLimits.SuspectReports;
        for (var i = 0; i + n - 1 < times.Count; i++)
            if (times[i + n - 1] - times[i] <= LiveChargingLimits.SuspectWindow) return true;
        return false;
    }
}

/// <summary>Per plug: open reports and the warning. Keyed by (OcppChargePoint.Id, ConnectorId).</summary>
public sealed record ConnectorReportStat(int OpenReports, bool SuspectedIssue, DateTime? LastReportAt);

public static class ConnectorReportStats
{
    public static async Task<Dictionary<(int ChargerId, int ConnectorId), ConnectorReportStat>> LoadAsync(IApplicationDbContext db,
        IReadOnlyCollection<int> chargerIds, DateTime nowUtc, CancellationToken ct)
    {
        var result = new Dictionary<(int, int), ConnectorReportStat>();
        if (chargerIds.Count == 0) return result;

        var open = await db.OcppConnectorReports.AsNoTracking()
            .Where(r => chargerIds.Contains(r.OcppChargePointId) && r.ResolvedAt == null && !r.IsDeleted)
            .Select(r => new { r.OcppChargePointId, r.ConnectorId, r.CreatedAt })
            .ToListAsync(ct);
        if (open.Count == 0) return result;

        var groups = open.GroupBy(r => (r.OcppChargePointId, r.ConnectorId)).ToList();

        // Only plugs that could be suspected need their last successful start.
        var candidates = groups.Where(g => g.Count() >= LiveChargingLimits.SuspectReports).Select(g => g.Key).ToList();
        var lastStarts = new Dictionary<(int, int), DateTime>();
        if (candidates.Count > 0)
        {
            var candidateChargers = candidates.Select(c => c.OcppChargePointId).Distinct().ToList();
            var since = open.Min(r => r.CreatedAt);
            var starts = await db.OcppTransactions.AsNoTracking()
                .Where(t => candidateChargers.Contains(t.OcppChargePointId) && !t.WasRejected && !t.IsOrphan && t.ReceivedStartAt >= since)
                .GroupBy(t => new { t.OcppChargePointId, t.ConnectorId })
                .Select(g => new { g.Key.OcppChargePointId, g.Key.ConnectorId, Last = g.Max(t => t.ReceivedStartAt) })
                .ToListAsync(ct);
            foreach (var s in starts) lastStarts[(s.OcppChargePointId, s.ConnectorId)] = s.Last;
        }

        foreach (var g in groups)
        {
            DateTime? last = lastStarts.TryGetValue(g.Key, out var l) ? l : null;
            result[g.Key] = new ConnectorReportStat(g.Count(), SuspectedIssueRule.IsSuspected(g.Select(r => r.CreatedAt), last), g.Max(r => r.CreatedAt));
        }
        return result;
    }
}

public record ConnectorReportCreatedDto(int ReportId, bool Duplicate, int OpenReports, bool SuspectedIssue);

/// <summary>
/// Driver (signed in): the plug did not start. Reason NoResponse | Rejected | ScreenError | Damaged | Other;
/// commandId = the RemoteStartTransaction from the start's answer, so the charger's own reply is attached.
/// A second report by the same driver on the same plug within 30 min returns the first one.
/// </summary>
public record ReportConnectorProblemCommand(int ChargingPointId, int ChargerId, int ConnectorId, string Reason, int? CommandId = null, string? Note = null)
    : IRequest<ConnectorReportCreatedDto>;

public class ReportConnectorProblemCommandValidator : AbstractValidator<ReportConnectorProblemCommand>
{
    public ReportConnectorProblemCommandValidator()
    {
        RuleFor(x => x.ConnectorId).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Reason).Must(r => OcppConnectorReportReason.Parse(r) is not null)
            .WithMessage(_ => Lang.Pick("السبب لازم يكون واحد من: ", "Reason must be one of: ") + string.Join(", ", OcppConnectorReportReason.All));
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public class ReportConnectorProblemCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<ReportConnectorProblemCommand, ConnectorReportCreatedDto>
{
    public async Task<ConnectorReportCreatedDto> Handle(ReportConnectorProblemCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;

        var plug = await db.OcppConnectors.AsNoTracking()
            .Where(k => k.OcppChargePointId == request.ChargerId && k.ConnectorId == request.ConnectorId
                        && k.ChargePoint.ChargingPointId == request.ChargingPointId && !k.ChargePoint.IsDeleted)
            .Select(k => new
            {
                k.Status, k.ErrorCode, k.StatusUpdatedAt, k.StatusReceivedAt,
                k.ChargePoint.IsEnabled, k.ChargePoint.IsConnected, k.ChargePoint.LastMessageAt, k.ChargePoint.DisconnectedAt, k.ChargePoint.HeartbeatInterval,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(Lang.Pick("ما لقينا هاد المقبس بهاي المحطة.", "Cannot find this plug at this station."));

        var since = now - LiveChargingLimits.ReportDedupWindow;
        var existing = await db.OcppConnectorReports.AsNoTracking()
            .Where(r => r.UserId == userId && r.OcppChargePointId == request.ChargerId && r.ConnectorId == request.ConnectorId
                        && r.ResolvedAt == null && !r.IsDeleted && r.CreatedAt >= since)
            .OrderByDescending(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(cancellationToken);

        int reportId;
        if (existing is int id)
        {
            reportId = id;
        }
        else
        {
            var command = request.CommandId is int commandId
                ? await db.OcppCommands.AsNoTracking()
                    .Where(c => c.Id == commandId && c.OcppChargePointId == request.ChargerId && !c.IsDeleted)
                    .Select(c => new { c.Id, c.Status, c.ResultStatus, c.ErrorCode })
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var report = new OcppConnectorReport
            {
                ChargingPointId = request.ChargingPointId,
                OcppChargePointId = request.ChargerId,
                ConnectorId = request.ConnectorId,
                UserId = userId,
                Reason = OcppConnectorReportReason.Parse(request.Reason)!,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                OcppCommandId = command?.Id,
                CommandStatus = command?.Status,
                CommandResultStatus = command?.ResultStatus,
                CommandErrorCode = command?.ErrorCode,
                ConnectorStatus = plug.Status,
                ConnectorErrorCode = plug.ErrorCode,
                ConnectorStatusAt = plug.StatusUpdatedAt ?? plug.StatusReceivedAt,
                ChargerOnline = plug.IsEnabled && OcppLiveness.StateOf(plug.IsConnected, plug.LastMessageAt, plug.DisconnectedAt, plug.HeartbeatInterval, now) == OcppLiveness.Online,
            };
            db.OcppConnectorReports.Add(report);
            await db.SaveChanges(cancellationToken);
            reportId = report.Id;

            var rid = report.Id;
            try { jobs.Enqueue<LiveChargingNotifications>(n => n.NotifyConnectorReportAsync(rid, CancellationToken.None)); }
            catch { /* the report is stored; the push is best-effort */ }
        }

        var stats = await ConnectorReportStats.LoadAsync(db, [request.ChargerId], now, cancellationToken);
        var stat = stats.GetValueOrDefault((request.ChargerId, request.ConnectorId));
        return new ConnectorReportCreatedDto(reportId, existing is not null, stat?.OpenReports ?? 0, stat?.SuspectedIssue ?? false);
    }
}

/// <summary>A report as the partner sees it: the reporter's name and masked phone only.</summary>
public record ConnectorReportDto(
    int Id,
    int ChargingPointId,
    int ChargerId,
    string? ChargerName,
    int ConnectorId,
    string Reason,
    string ReasonText,
    string? Note,
    int? CommandId,
    string? CommandStatus,
    string? CommandResultStatus,
    string? CommandErrorCode,
    string? ConnectorStatus,
    string? ConnectorErrorCode,
    DateTime? ConnectorStatusAt,
    bool ChargerOnline,
    SessionDriverDto? Reporter,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolvedByName,
    string? ResolutionNote);

internal static class ConnectorReportProjection
{
    public static async Task<List<ConnectorReportDto>> MapAsync(IApplicationDbContext db, List<OcppConnectorReport> rows, CancellationToken ct)
    {
        var userIds = rows.Select(r => r.UserId).Concat(rows.Where(r => r.ResolvedBy != null).Select(r => r.ResolvedBy!.Value)).Distinct().ToList();
        var users = await db.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.Phone }).ToDictionaryAsync(u => u.Id, ct);
        var chargerIds = rows.Select(r => r.OcppChargePointId).Distinct().ToList();
        var chargers = await db.OcppChargePoints.AsNoTracking().Where(c => chargerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName, c.ChargePointId }).ToDictionaryAsync(c => c.Id, ct);

        return rows.Select(r =>
        {
            var reporter = users.GetValueOrDefault(r.UserId);
            var charger = chargers.GetValueOrDefault(r.OcppChargePointId);
            return new ConnectorReportDto(r.Id, r.ChargingPointId, r.OcppChargePointId, charger?.DisplayName ?? charger?.ChargePointId, r.ConnectorId,
                r.Reason, Lang.Pick(OcppConnectorReportReason.DescribeAr(r.Reason), OcppConnectorReportReason.Describe(r.Reason)), r.Note,
                r.OcppCommandId, r.CommandStatus, r.CommandResultStatus, r.CommandErrorCode, r.ConnectorStatus, r.ConnectorErrorCode, r.ConnectorStatusAt, r.ChargerOnline,
                reporter is null ? null : new SessionDriverDto(reporter.Name, DriverPrivacy.MaskPhone(reporter.Phone)),
                r.CreatedAt, r.ResolvedAt, r.ResolvedBy is int by ? users.GetValueOrDefault(by)?.Name : null, r.ResolutionNote);
        }).ToList();
    }

    public static IQueryable<OcppConnectorReport> ByStatus(IQueryable<OcppConnectorReport> q, string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "resolved" => q.Where(r => r.ResolvedAt != null),
            "all" => q,
            _ => q.Where(r => r.ResolvedAt == null),
        };
}

/// <summary>Owner / manager (ConnectView): reports on the station's plugs, newest first. status = open (default) | resolved | all.</summary>
public record GetStationConnectorReportsRequest(int ChargingPointId, string? Status = null, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<ConnectorReportDto>>;

public class GetStationConnectorReportsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetStationConnectorReportsRequest, PagedResult<ConnectorReportDto>>
{
    public async Task<PagedResult<ConnectorReportDto>> Handle(GetStationConnectorReportsRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken,
            requiredPrivilege: WorkerPrivileges.ConnectView);
        var q = ConnectorReportProjection.ByStatus(
            db.OcppConnectorReports.AsNoTracking().Where(r => r.ChargingPointId == request.ChargingPointId && !r.IsDeleted), request.Status);
        var page = await q.OrderByDescending(r => r.Id).ToPaginatedAsync(request.Page, request.PageSize, 20, 200, cancellationToken);
        return page.As(await ConnectorReportProjection.MapAsync(db, page.Items, cancellationToken));
    }
}

/// <summary>
/// Owner / manager (ConnectControl) or admin: "fixed". By default every open report on that plug is resolved
/// (the plug is what was fixed); allOnPlug=false resolves only this one. Returns how many were resolved.
/// </summary>
public record ResolveConnectorReportCommand(int ReportId, string? Note = null, bool AllOnPlug = true) : IRequest<int>;

public class ResolveConnectorReportCommandValidator : AbstractValidator<ResolveConnectorReportCommand>
{
    public ResolveConnectorReportCommandValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public class ResolveConnectorReportCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ResolveConnectorReportCommand, int>
{
    public async Task<int> Handle(ResolveConnectorReportCommand request, CancellationToken cancellationToken)
    {
        var report = await db.OcppConnectorReports.AsNoTracking()
            .Where(r => r.Id == request.ReportId && !r.IsDeleted)
            .Select(r => new { r.ChargingPointId, r.OcppChargePointId, r.ConnectorId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("cannot find report " + request.ReportId);
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, report.ChargingPointId, cancellationToken,
            requiredPrivilege: WorkerPrivileges.ConnectControl);

        var rows = await db.OcppConnectorReports
            .Where(r => !r.IsDeleted && r.ResolvedAt == null
                        && (request.AllOnPlug
                            ? r.OcppChargePointId == report.OcppChargePointId && r.ConnectorId == report.ConnectorId
                            : r.Id == request.ReportId))
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        foreach (var r in rows)
        {
            r.ResolvedAt = now;
            r.ResolvedBy = currentUser.UserId;
            r.ResolutionNote = note;
        }
        if (rows.Count > 0) await db.SaveChanges(cancellationToken);
        return rows.Count;
    }
}

/// <summary>Admin: reports grouped per plug ("3 reports on this plug today"), the busiest plugs first.</summary>
public record ConnectorReportGroupDto(
    int ChargingPointId,
    string StationName,
    int ChargerId,
    string? ChargerName,
    string ChargePointId,
    int ConnectorId,
    int OpenReports,
    int ReportsToday,
    int TotalReports,
    bool SuspectedIssue,
    DateTime LastReportAt,
    string LastReason,
    string? LastCommandResult,
    List<ConnectorReportDto> Latest);

/// <param name="Status">open (default: plugs with open reports) | all.</param>
/// <param name="From">Reports created from (Jordan time when no offset).</param>
public record GetAdminConnectorReportsRequest(string? Status = null, int? ChargingPointId = null, DateTime? From = null, DateTime? To = null)
    : IRequest<List<ConnectorReportGroupDto>>;

public class GetAdminConnectorReportsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminConnectorReportsRequest, List<ConnectorReportGroupDto>>
{
    public async Task<List<ConnectorReportGroupDto>> Handle(GetAdminConnectorReportsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var now = DateTime.UtcNow;
        var todayStartUtc = JordanTime.ToUtc(JordanTime.FromUtc(now).Date);

        var q = db.OcppConnectorReports.AsNoTracking().Where(r => !r.IsDeleted);
        if (request.ChargingPointId is int cp) q = q.Where(r => r.ChargingPointId == cp);
        if (JordanTime.ToUtc(request.From) is DateTime from) q = q.Where(r => r.CreatedAt >= from);
        if (JordanTime.ToUtc(request.To) is DateTime to) q = q.Where(r => r.CreatedAt < to);
        if (!string.Equals(request.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            // Plugs that have at least one open report — with their whole recent history for the counts.
            var openPlugs = db.OcppConnectorReports.Where(o => o.ResolvedAt == null && !o.IsDeleted);
            q = q.Where(r => openPlugs.Any(o => o.OcppChargePointId == r.OcppChargePointId && o.ConnectorId == r.ConnectorId));
        }

        var rows = await q.OrderByDescending(r => r.Id).Take(2000).ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];

        var mapped = (await ConnectorReportProjection.MapAsync(db, rows, cancellationToken)).ToDictionary(d => d.Id);
        var stationIds = rows.Select(r => r.ChargingPointId).Distinct().ToList();
        var stations = await db.ChargingPoints.AsNoTracking().Where(c => stationIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var chargerIds = rows.Select(r => r.OcppChargePointId).Distinct().ToList();
        var ocppIds = await db.OcppChargePoints.AsNoTracking().Where(c => chargerIds.Contains(c.Id)).Select(c => new { c.Id, c.ChargePointId }).ToDictionaryAsync(c => c.Id, c => c.ChargePointId, cancellationToken);
        var stats = await ConnectorReportStats.LoadAsync(db, chargerIds, now, cancellationToken);

        return rows.GroupBy(r => (r.OcppChargePointId, r.ConnectorId))
            .Select(g =>
            {
                var latest = g.OrderByDescending(r => r.CreatedAt).ToList();
                var last = latest[0];
                var stat = stats.GetValueOrDefault(g.Key);
                return new ConnectorReportGroupDto(last.ChargingPointId, stations.GetValueOrDefault(last.ChargingPointId) ?? "", g.Key.OcppChargePointId,
                    mapped[last.Id].ChargerName, ocppIds.GetValueOrDefault(g.Key.OcppChargePointId) ?? "", g.Key.ConnectorId,
                    stat?.OpenReports ?? 0, g.Count(r => r.CreatedAt >= todayStartUtc), g.Count(), stat?.SuspectedIssue ?? false,
                    last.CreatedAt, last.Reason, last.CommandResultStatus ?? last.CommandStatus,
                    latest.Take(5).Select(r => mapped[r.Id]).ToList());
            })
            .OrderByDescending(g => g.OpenReports).ThenByDescending(g => g.ReportsToday).ThenByDescending(g => g.LastReportAt)
            .ToList();
    }
}
