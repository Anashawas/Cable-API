using System.Globalization;
using System.Text;
using Application.Common.Security;
using Application.Ocpp.Commands.ChargeLimits;
using Application.Ocpp.Queries;
using Application.Ocpp.Queries.GetSessionLive;
using Application.Settings;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §11.b — every charging session for the admin: one list with filters and totals, the full detail (driver with
// the full phone, who started / stopped it, card, commands, curve), a CSV export and the user-profile tab.
// ---------------------------------------------------------------------------

/// <param name="From">Started at or after (Jordan time when no offset is given).</param>
/// <param name="To">Started before (Jordan time when no offset is given).</param>
/// <param name="Source">Start method: App | Sticker | Card | Operator.</param>
/// <param name="Status">open | closed.</param>
/// <param name="LimitKind">Jod | Kwh | Soc, or none (no limit).</param>
/// <param name="Q">Session id, driver / staff name, phone, idTag or card label.</param>
public record AdminSessionFilter(
    DateTime? From = null,
    DateTime? To = null,
    int? ChargingPointId = null,
    int? ChargerId = null,
    int? UserId = null,
    string? Source = null,
    string? Status = null,
    string? LimitKind = null,
    string? Q = null);

public record AdminSessionPersonDto(int UserId, string? Name, string? Phone);

public record AdminSessionRowDto(
    int Id,
    int ChargingPointId,
    string StationName,
    int ChargerId,
    string? ChargerName,
    string ChargePointId,
    int ConnectorId,
    DateTime StartedAt,
    DateTime? StoppedAt,
    int DurationSec,
    bool IsOpen,
    bool IsStale,
    string StartSource,
    /// <summary>App | Sticker | Card | Operator.</summary>
    string StartMethod,
    string IdTag,
    /// <summary>App / sticker starts and cards linked to a user: the driver, full phone (admin only).</summary>
    AdminSessionPersonDto? Driver,
    /// <summary>Operator starts: the admin / owner / worker who started it.</summary>
    AdminSessionPersonDto? StartedByStaff,
    string? CardLabel,
    decimal? EnergyKwh,
    int? CostFils,
    decimal? CostJod,
    int? SocStartPercent,
    int? SocEndPercent,
    ChargeLimitDto? ChargeLimit,
    string? StopReason,
    string? StopReasonText,
    string? StopReasonTextAr,
    string? ReceiptNo,
    /// <summary>"Problem with this session" complaints still open.</summary>
    int OpenIssues,
    bool WasRejected = false,
    bool IsOrphan = false,
    /// <summary>The driver's car on an app start (13.4).</summary>
    string? CarName = null,
    /// <summary>Automatic points the driver got (4.7); null = none / not decided.</summary>
    int? PointsAwarded = null,
    /// <summary>App start: how far the phone was from the station, in metres (4.2 / 11.c).</summary>
    int? StartDistanceMeters = null,
    /// <summary>
    /// §11.2: the session carries a cheap alert — started far away (over 80% of Ocpp.AppStart.MaxDistanceMeters),
    /// the car stayed parked after charging, or stale. The detail lists every alert (alerts[]).
    /// </summary>
    bool HasAlerts = false);

public record AdminSessionTotalsDto(int Sessions, decimal EnergyKwh, int CostFils, decimal CostJod, int OpenNow);

/// <summary>A page of sessions plus the totals of everything the filters match (openNow ignores the status and date filters).</summary>
public record AdminSessionsPageDto(List<AdminSessionRowDto> Items, int TotalCount, int Page, int PageSize, AdminSessionTotalsDto Totals)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1 && TotalCount > 0;
}

public record AdminSessionIssueDto(int IssueId, string? Reason, string Note, string Status, int StatusCode, string? Reply, DateTime CreatedAt);

public record AdminSessionDetailDto(
    AdminSessionRowDto Session,
    /// <summary>The live / final figures: energy, cost and price lines, battery, limit, stop text and the readings curve (series).</summary>
    SessionLiveDto Live,
    string? StationAddress,
    string? PlugTypeName,
    decimal? PowerKw,
    string? LimitSetByName,
    /// <summary>Who sent the RemoteStop that ended it (null = stopped at the charger / by the car / by the limit).</summary>
    string? StoppedByName,
    bool StoppedByLimit,
    /// <summary>The RemoteStartTransaction(s) that opened it (failed attempts included) and the RemoteStop(s) sent for it.</summary>
    List<OcppCommandDto> Commands,
    List<AdminSessionIssueDto> Issues,
    /// <summary>Automatic loyalty points awarded for the session (4.7, Ocpp.AutoPoints.Enabled); null = none / not decided.</summary>
    int? PointsAwarded = null,
    /// <summary>§11.2 alerts: startedFarAway, repeatedFailedStarts, parkedAfterCharging, limitStopNotAccepted, stale. Empty = nothing to look at.</summary>
    List<AdminSessionAlertDto>? Alerts = null);

/// <summary>
/// §11.2: one thing the admin should look at on a session. <see cref="Code"/> is stable (startedFarAway |
/// repeatedFailedStarts | parkedAfterCharging | limitStopNotAccepted | stale); <see cref="Text"/> follows
/// Accept-Language, <see cref="TextAr"/> / <see cref="TextEn"/> carry both.
/// </summary>
public record AdminSessionAlertDto(string Code, string Text, string TextAr, string TextEn);

/// <summary>§11.2: the alert codes, their rules and texts.</summary>
public static class AdminSessionAlerts
{
    public const string StartedFarAway = "startedFarAway";
    public const string RepeatedFailedStarts = "repeatedFailedStarts";
    public const string ParkedAfterCharging = "parkedAfterCharging";
    public const string LimitStopNotAccepted = "limitStopNotAccepted";
    public const string Stale = "stale";

    /// <summary>A start counts as "far away" past this share of Ocpp.AppStart.MaxDistanceMeters.</summary>
    public const decimal FarAwayShare = 0.8m;

    /// <summary>Failed RemoteStarts by the same driver on the same station in this window before the session raise repeatedFailedStarts.</summary>
    public static readonly TimeSpan FailedStartsWindow = TimeSpan.FromMinutes(30);
    public const int FailedStartsThreshold = 2;

    /// <summary>How the limit enforcer (Cable.Ocpp ChargeLimitEnforcer) marks its own RemoteStop in OcppCommand.ErrorDescription.</summary>
    public const string LimitStopMarker = "Charge limit reached";

    public static bool IsFarAway(int? startDistanceMeters, int maxDistanceMeters) =>
        startDistanceMeters is int d && maxDistanceMeters > 0 && d > maxDistanceMeters * FarAwayShare;

    /// <summary>The list flag: only what the row already carries (far away, parked, stale).</summary>
    public static bool HasCheapAlert(int? startDistanceMeters, int maxDistanceMeters, DateTime? parkedNotifiedAt, bool isStale) =>
        IsFarAway(startDistanceMeters, maxDistanceMeters) || parkedNotifiedAt is not null || isStale;

    private static AdminSessionAlertDto Make(string code, string ar, string en) => new(code, Lang.Pick(ar, en), ar, en);

    public static AdminSessionAlertDto FarAway(int distanceMeters, int maxMeters) => Make(StartedFarAway,
        $"بدأ الشحن من التطبيق على بعد {distanceMeters} م من المحطة (الحد {maxMeters} م).",
        $"Started from the app {distanceMeters} m from the station (the limit is {maxMeters} m).");

    public static AdminSessionAlertDto FailedStarts(int count) => Make(RepeatedFailedStarts,
        $"نفس السائق حاول يبدأ الشحن {count} مرات بدون نجاح على هاي المحطة خلال 30 دقيقة قبل هاي الجلسة.",
        $"The same driver had {count} failed start attempts on this station in the 30 minutes before this session.");

    public static AdminSessionAlertDto Parked(DateTime notifiedAtUtc)
    {
        var at = JordanTime.FromUtc(notifiedAtUtc).ToString("HH:mm", CultureInfo.InvariantCulture);
        return Make(ParkedAfterCharging,
            $"السيارة ضلت على المقبس بعد ما خلص الشحن (انبعت للسائق تنبيه الساعة {at}).",
            $"The car stayed on the plug after charging finished (the driver was alerted at {at}).");
    }

    public static AdminSessionAlertDto LimitStop(int count) => Make(LimitStopNotAccepted,
        count == 1
            ? "الشاحن ما قبل أمر الإيقاف اللي انبعت عند الوصول للحد."
            : $"الشاحن ما قبل أمر الإيقاف اللي انبعت عند الوصول للحد ({count} مرات).",
        count == 1
            ? "The charger did not accept the stop sent when the limit was reached."
            : $"The charger did not accept the stop sent when the limit was reached ({count} times).");

    public static AdminSessionAlertDto StaleSession() => Make(Stale,
        "الجلسة مفتوحة أكثر من 24 ساعة بدون إيقاف من الشاحن — بدها تسوية يدوية.",
        "Open for more than 24 h without a stop from the charger — needs manual reconciliation.");
}

internal static class AdminSessionQuery
{
    public static IQueryable<OcppTransaction> Filter(IApplicationDbContext db, AdminSessionFilter f, bool withStatusAndDates, bool includeRejected = false)
    {
        var q = db.OcppTransactions.AsNoTracking().Where(t => !t.ChargePoint.IsDeleted);
        if (!includeRejected) q = q.Where(t => !t.WasRejected && !t.IsOrphan);
        if (f.ChargingPointId is int cp) q = q.Where(t => t.ChargePoint.ChargingPointId == cp);
        if (f.ChargerId is int ch) q = q.Where(t => t.OcppChargePointId == ch);
        if (f.UserId is int uid) q = q.Where(t => t.StartedByUserId == uid);

        var method = OcppStartMethod.Parse(f.Source);
        if (method is not null) q = q.Where(t => (t.StartMethod ?? t.StartSource) == method);

        if (!string.IsNullOrWhiteSpace(f.LimitKind))
        {
            var kind = f.LimitKind.Trim();
            q = string.Equals(kind, "none", StringComparison.OrdinalIgnoreCase)
                ? q.Where(t => t.ChargeLimitKind == null)
                : q.Where(t => t.ChargeLimitKind == kind);
        }

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = f.Q.Trim();
            var id = int.TryParse(term.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            var digits = new string(PhoneNumberUtility.ToAsciiDigits(term).Where(char.IsDigit).ToArray());
            var phone = digits.Length >= 4 ? PhoneNumberUtility.NormalizePhoneNumber(digits) ?? digits.TrimStart('0') : null;
            q = q.Where(t => t.Id == id
                             || t.IdTag.Contains(term)
                             || db.UserAccounts.Any(u => u.Id == t.StartedByUserId
                                                         && ((u.Name != null && u.Name.Contains(term)) || (phone != null && u.Phone != null && u.Phone.Contains(phone))))
                             || db.OcppAuthorizedTags.Any(a => a.ChargingPointId == t.ChargePoint.ChargingPointId && !a.IsDeleted && a.IdTag == t.IdTag
                                                               && a.Label != null && a.Label.Contains(term)));
        }

        if (withStatusAndDates)
        {
            q = PartnerSessionFilters.ApplyStatus(q, f.Status);
            if (JordanTime.ToUtc(f.From) is DateTime from) q = q.Where(t => t.StartedAt >= from);
            if (JordanTime.ToUtc(f.To) is DateTime to) q = q.Where(t => t.StartedAt < to);
        }
        return q;
    }

    private sealed record Raw(
        int Id, int ChargingPointId, string StationName, int ChargerId, string? ChargerName, string ChargePointId, int ConnectorId,
        DateTime StartedAt, DateTime? StoppedAt, bool IsOpen, bool IsStale, string StartSource, string StartMethod, string IdTag, int? StartedByUserId,
        string? UserName, string? UserPhone, string? CardLabel, decimal? EnergyKwh, int? CostFils, int? SocStartPercent, int? SocEndPercent,
        string? ChargeLimitKind, decimal? ChargeLimitValue, DateTime? ChargeLimitSetAt, DateTime? ChargeLimitReachedAt, string? StopReason,
        string? ReceiptNo, int OpenIssues, bool WasRejected, bool IsOrphan, int? CarId, int? PointsAwarded, int? StartDistanceMeters,
        DateTime? ParkedNotifiedAt);

    private static IQueryable<Raw> Project(IApplicationDbContext db, IQueryable<OcppTransaction> q) =>
        q.Select(t => new Raw(
            t.Id, t.ChargePoint.ChargingPointId, t.ChargePoint.ChargingPoint.Name, t.OcppChargePointId, t.ChargePoint.DisplayName, t.ChargePoint.ChargePointId, t.ConnectorId,
            t.StartedAt, t.StoppedAt, t.IsOpen, t.IsStale, t.StartSource, t.StartMethod ?? t.StartSource, t.IdTag, t.StartedByUserId,
            db.UserAccounts.Where(u => u.Id == t.StartedByUserId).Select(u => u.Name).FirstOrDefault(),
            db.UserAccounts.Where(u => u.Id == t.StartedByUserId).Select(u => u.Phone).FirstOrDefault(),
            db.OcppAuthorizedTags.Where(a => a.ChargingPointId == t.ChargePoint.ChargingPointId && a.IdTag == t.IdTag && !a.IsDeleted).Select(a => a.Label).FirstOrDefault(),
            t.EnergyKwh, t.CostFils, t.SocStartPercent, t.SocEndPercent,
            t.ChargeLimitKind, t.ChargeLimitValue, t.ChargeLimitSetAt, t.ChargeLimitReachedAt, t.StopReason,
            t.ReceiptNo,
            db.UserComplaints.Count(c => c.OcppTransactionId == t.Id && !c.IsDeleted
                                         && (c.Status == 0 || c.Status == 3 || c.Status == 4)),
            t.WasRejected, t.IsOrphan, t.CarId, t.PointsAwarded, t.StartDistanceMeters, t.ParkedNotifiedAt));

    private static AdminSessionRowDto ToRow(Raw r, DateTime now, IReadOnlyDictionary<int, SessionCarDto> cars, int maxDistanceMeters)
    {
        var person = r.StartedByUserId is int uid ? new AdminSessionPersonDto(uid, r.UserName, PhoneNumberUtility.ToE164OrOriginal(r.UserPhone)) : null;
        var staff = r.StartSource == OcppStartSource.Operator;
        var stop = ChargeLimitDto.StopText(r.StopReason, r.ChargeLimitKind, r.ChargeLimitValue, r.ChargeLimitReachedAt);
        return new AdminSessionRowDto(r.Id, r.ChargingPointId, r.StationName, r.ChargerId, r.ChargerName, r.ChargePointId, r.ConnectorId,
            r.StartedAt, r.StoppedAt, (int)((r.StoppedAt ?? now) - r.StartedAt).TotalSeconds, r.IsOpen, r.IsStale, r.StartSource, r.StartMethod, r.IdTag,
            staff ? null : person, staff ? person : null, r.StartSource == OcppStartSource.Card ? r.CardLabel : null,
            r.EnergyKwh, r.CostFils, r.CostFils is int f ? f / 1000m : null, r.SocStartPercent, r.SocEndPercent,
            ChargeLimitDto.From(r.ChargeLimitKind, r.ChargeLimitValue, r.ChargeLimitSetAt, r.ChargeLimitReachedAt),
            r.StopReason, stop.En, stop.Ar, r.ReceiptNo, r.OpenIssues, r.WasRejected, r.IsOrphan,
            r.CarId is int carId ? cars.GetValueOrDefault(carId)?.Name : null, r.PointsAwarded, r.StartDistanceMeters,
            AdminSessionAlerts.HasCheapAlert(r.StartDistanceMeters, maxDistanceMeters, r.ParkedNotifiedAt, r.IsStale));
    }

    public static async Task<PagedResult<AdminSessionRowDto>> PageAsync(IApplicationDbContext db, IQueryable<OcppTransaction> q, int? page, int? pageSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var raw = await Project(db, q.OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.Id)).ToPaginatedAsync(page, pageSize, 20, 200, ct);
        var cars = await SessionCars.LoadAsync(db, raw.Items.Select(r => r.CarId), ct);
        var maxDistance = (await AppSettingsProvider.GetOcppAppStartAsync(db, ct)).MaxDistanceMeters;
        return raw.As(raw.Items.Select(r => ToRow(r, now, cars, maxDistance)).ToList());
    }

    public static async Task<List<AdminSessionRowDto>> AllAsync(IApplicationDbContext db, IQueryable<OcppTransaction> q, int max, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var raw = await Project(db, q.OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.Id)).Take(max).ToListAsync(ct);
        var cars = await SessionCars.LoadAsync(db, raw.Select(r => r.CarId), ct);
        var maxDistance = (await AppSettingsProvider.GetOcppAppStartAsync(db, ct)).MaxDistanceMeters;
        return raw.Select(r => ToRow(r, now, cars, maxDistance)).ToList();
    }

    public static async Task<AdminSessionTotalsDto> TotalsAsync(IApplicationDbContext db, AdminSessionFilter f, IQueryable<OcppTransaction> q, CancellationToken ct)
    {
        var count = await q.CountAsync(ct);
        var energy = await q.SumAsync(t => t.EnergyKwh ?? 0m, ct);
        var cost = await q.SumAsync(t => t.CostFils ?? 0, ct);
        var openNow = await Filter(db, f, withStatusAndDates: false).CountAsync(t => t.IsOpen && !t.IsStale, ct);
        return new AdminSessionTotalsDto(count, energy, cost, cost / 1000m, openNow);
    }
}

/// <summary>Admin: all sessions, newest first, with filters and totals.</summary>
public record GetAdminOcppSessionsRequest(AdminSessionFilter Filter, int? Page = null, int? PageSize = null) : IRequest<AdminSessionsPageDto>;

public class GetAdminOcppSessionsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminOcppSessionsRequest, AdminSessionsPageDto>
{
    public async Task<AdminSessionsPageDto> Handle(GetAdminOcppSessionsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var q = AdminSessionQuery.Filter(db, request.Filter, withStatusAndDates: true);
        var page = await AdminSessionQuery.PageAsync(db, q, request.Page, request.PageSize, cancellationToken);
        var totals = await AdminSessionQuery.TotalsAsync(db, request.Filter, q, cancellationToken);
        return new AdminSessionsPageDto(page.Items, page.TotalCount, page.Page, page.PageSize, totals);
    }
}

/// <summary>Admin: CSV (UTF-8 with BOM so Excel shows Arabic) of the sessions the filters match, at most 50,000 rows. Times in Jordan time.</summary>
public record ExportAdminOcppSessionsRequest(AdminSessionFilter Filter) : IRequest<LiveChargingFile>;

public class ExportAdminOcppSessionsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ExportAdminOcppSessionsRequest, LiveChargingFile>
{
    public const int MaxRows = 50_000;

    public async Task<LiveChargingFile> Handle(ExportAdminOcppSessionsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var rows = await AdminSessionQuery.AllAsync(db, AdminSessionQuery.Filter(db, request.Filter, withStatusAndDates: true), MaxRows, cancellationToken);
        var csv = SessionCsv.Write(rows);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return new LiveChargingFile(bytes, "text/csv; charset=utf-8", $"charging-sessions-{JordanTime.FromUtc(DateTime.UtcNow):yyyyMMdd-HHmm}.csv");
    }
}

/// <summary>Pure CSV writer for the admin export.</summary>
public static class SessionCsv
{
    private static readonly string[] Header =
    [
        "SessionId", "StartedAt", "StoppedAt", "DurationMin", "Status", "Station", "StationId", "Charger", "ChargePointId", "Plug",
        "StartMethod", "Driver", "DriverPhone", "UserId", "StartedByStaff", "IdTag", "Card", "EnergyKwh", "CostJod",
        "BatteryStart", "BatteryEnd", "LimitKind", "LimitValue", "LimitReachedAt", "StopReason", "ReceiptNo", "OpenIssues",
    ];

    public static string Write(IEnumerable<AdminSessionRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Header));
        foreach (var r in rows)
        {
            string[] cells =
            [
                r.Id.ToString(CultureInfo.InvariantCulture),
                Local(r.StartedAt), r.StoppedAt is DateTime s ? Local(s) : "",
                (r.DurationSec / 60).ToString(CultureInfo.InvariantCulture),
                r.IsOpen ? "open" : "closed",
                r.StationName, r.ChargingPointId.ToString(CultureInfo.InvariantCulture), r.ChargerName ?? "", r.ChargePointId,
                r.ConnectorId.ToString(CultureInfo.InvariantCulture),
                r.StartMethod, r.Driver?.Name ?? "", r.Driver?.Phone ?? "", r.Driver?.UserId.ToString(CultureInfo.InvariantCulture) ?? "",
                r.StartedByStaff?.Name ?? "", r.IdTag, r.CardLabel ?? "",
                r.EnergyKwh?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                r.CostJod?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                r.SocStartPercent?.ToString(CultureInfo.InvariantCulture) ?? "", r.SocEndPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.ChargeLimit?.Kind ?? "", r.ChargeLimit?.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
                r.ChargeLimit?.ReachedAt is DateTime reached ? Local(reached) : "",
                r.StopReasonText ?? r.StopReason ?? "", r.ReceiptNo ?? "", r.OpenIssues.ToString(CultureInfo.InvariantCulture),
            ];
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        return sb.ToString();
    }

    private static string Local(DateTime utc) => JordanTime.FromUtc(utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>RFC 4180 quoting; a leading = + - @ is prefixed with ' so spreadsheet apps never run it as a formula.</summary>
    public static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

/// <summary>Admin: one session with everything — driver with full phone, who started / stopped it, card, plug, cost lines, battery, limit, commands, curve, issues.</summary>
public record GetAdminOcppSessionRequest(int TransactionId) : IRequest<AdminSessionDetailDto>;

public class GetAdminOcppSessionRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminOcppSessionRequest, AdminSessionDetailDto>
{
    public async Task<AdminSessionDetailDto> Handle(GetAdminOcppSessionRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var q = db.OcppTransactions.AsNoTracking().Where(t => t.Id == request.TransactionId);
        var row = (await AdminSessionQuery.AllAsync(db, q, 1, cancellationToken)).FirstOrDefault()
                  ?? throw new NotFoundException("cannot find session " + request.TransactionId);
        var live = await SessionLiveBuilder.BuildAsync(db, row.Id, null, cancellationToken);

        var extra = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == row.Id)
            .Select(t => new
            {
                t.ChargePoint.ChargingPoint.Address,
                t.ChargePoint.ChargingPoint.CityName,
                t.ReceivedStartAt,
                t.ChargeLimitSetBy,
                t.ParkedNotifiedAt,
                Plug = db.OcppConnectors.Where(k => k.OcppChargePointId == t.OcppChargePointId && k.ConnectorId == t.ConnectorId)
                    .Select(k => new { PlugTypeName = k.PlugType != null ? k.PlugType.Name : null, k.PowerKw }).FirstOrDefault(),
            })
            .FirstAsync(cancellationToken);

        var commands = await SessionCommandsAsync(row, extra.ReceivedStartAt, cancellationToken);
        var stopCommand = commands.Where(c => c.Action == "RemoteStopTransaction" && c.Status == "Answered" && c.ResultStatus == "Accepted")
            .OrderBy(c => c.CreatedAt).FirstOrDefault();
        var stoppedByLimit = row.ChargeLimit?.ReachedAt is not null && row.StopReason == "Remote";

        var limitSetBy = extra.ChargeLimitSetBy is int by
            ? await db.UserAccounts.AsNoTracking().Where(u => u.Id == by).Select(u => u.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var issues = (await db.UserComplaints.AsNoTracking()
                .Where(c => c.OcppTransactionId == row.Id && !c.IsDeleted)
                .OrderByDescending(c => c.Id)
                .Select(c => new { c.Id, c.Reason, c.Note, c.Status, c.Reply, c.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(c => new AdminSessionIssueDto(c.Id, c.Reason, c.Note, SessionIssueStatus.Of(c.Status), c.Status, c.Reply, c.CreatedAt))
            .ToList();

        var alerts = await AlertsAsync(row, extra.ReceivedStartAt, extra.ParkedNotifiedAt, commands, cancellationToken);

        var address = string.Join(" · ", new[] { extra.Address, extra.CityName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new AdminSessionDetailDto(row, live, address.Length == 0 ? null : address, extra.Plug?.PlugTypeName, extra.Plug?.PowerKw,
            limitSetBy, stoppedByLimit ? null : stopCommand?.RequestedByName, stoppedByLimit, commands, issues, live.PointsAwarded, alerts);
    }

    /// <summary>§11.2: every alert on the session, in a fixed order.</summary>
    private async Task<List<AdminSessionAlertDto>> AlertsAsync(AdminSessionRowDto row, DateTime receivedStartAt, DateTime? parkedNotifiedAt,
        IReadOnlyList<OcppCommandDto> commands, CancellationToken ct)
    {
        var alerts = new List<AdminSessionAlertDto>();

        var maxDistance = (await AppSettingsProvider.GetOcppAppStartAsync(db, ct)).MaxDistanceMeters;
        if (AdminSessionAlerts.IsFarAway(row.StartDistanceMeters, maxDistance))
            alerts.Add(AdminSessionAlerts.FarAway(row.StartDistanceMeters!.Value, maxDistance));

        // The same driver's RemoteStarts on any charger of this station in the 30 min before, that the unit
        // refused or never answered (the one that opened this session was accepted, so it never counts).
        if (row.Driver?.UserId is int driverId)
        {
            var needle = "\"idTag\":\"" + OcppVirtualTag.ForUser(driverId) + "\"";
            var from = receivedStartAt - AdminSessionAlerts.FailedStartsWindow;
            var failed = await db.OcppCommands.AsNoTracking()
                .CountAsync(c => c.ChargePoint.ChargingPointId == row.ChargingPointId && !c.IsDeleted
                                 && c.Action == "RemoteStartTransaction"
                                 && c.CreatedAt >= from && c.CreatedAt < receivedStartAt
                                 && (c.CreatedBy == driverId || c.RequestPayload.Contains(needle))
                                 && !(c.Status == "Answered" && c.ResultStatus == "Accepted"), ct);
            if (failed >= AdminSessionAlerts.FailedStartsThreshold)
                alerts.Add(AdminSessionAlerts.FailedStarts(failed));
        }

        if (parkedNotifiedAt is DateTime parked)
            alerts.Add(AdminSessionAlerts.Parked(parked));

        // The limit enforcer audits its own stop with "Charge limit reached…" in the description.
        var limitStopsRefused = commands.Count(c => c.Action == "RemoteStopTransaction"
                                                    && c.ErrorDescription != null
                                                    && c.ErrorDescription.StartsWith(AdminSessionAlerts.LimitStopMarker, StringComparison.Ordinal)
                                                    && !(c.Status == "Answered" && c.ResultStatus == "Accepted"));
        if (limitStopsRefused > 0)
            alerts.Add(AdminSessionAlerts.LimitStop(limitStopsRefused));

        if (row.IsStale)
            alerts.Add(AdminSessionAlerts.StaleSession());

        return alerts;
    }

    /// <summary>
    /// RemoteStartTransaction with this session's idTag sent to its charger in the authorize window before the start
    /// (failed attempts too), and every RemoteStopTransaction quoting its id.
    /// </summary>
    private async Task<List<OcppCommandDto>> SessionCommandsAsync(AdminSessionRowDto row, DateTime receivedStartAt, CancellationToken ct)
    {
        var tag = OcppCredentials.NormalizeIdTag(row.IdTag);
        var startNeedle = "\"idTag\":\"" + tag + "\"";
        var stopNeedleA = "\"transactionId\":" + row.Id.ToString(CultureInfo.InvariantCulture) + "}";
        var stopNeedleB = "\"transactionId\":" + row.Id.ToString(CultureInfo.InvariantCulture) + ",";
        var from = receivedStartAt - OcppVirtualTag.RemoteStartAuthorizeWindow;
        var until = receivedStartAt.AddMinutes(2);

        var rows = await db.OcppCommands.AsNoTracking()
            .Where(c => c.OcppChargePointId == row.ChargerId && !c.IsDeleted
                        && ((c.Action == "RemoteStartTransaction" && c.CreatedAt >= from && c.CreatedAt <= until && c.RequestPayload.Contains(startNeedle))
                            || (c.Action == "RemoteStopTransaction" && (c.RequestPayload.Contains(stopNeedleA) || c.RequestPayload.Contains(stopNeedleB)))))
            .OrderBy(c => c.Id)
            .Take(50)
            .Select(c => new OcppCommandDto(
                c.Id, c.Action, c.RequestPayload, c.Status, c.ResultStatus, c.ResponsePayload,
                c.ErrorCode, c.ErrorDescription, c.DurationMs, c.CreatedBy,
                db.UserAccounts.Where(u => u.Id == c.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                c.CreatedAt, c.CompletedAt, null))
            .ToListAsync(ct);
        return rows.Select(d => d with { ConfirmedAfterSec = d.CompletedAt is DateTime done ? (int)Math.Max(0, (done - d.CreatedAt).TotalSeconds) : null }).ToList();
    }
}
