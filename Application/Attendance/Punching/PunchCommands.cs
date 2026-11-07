using Application.Attendance.Shifts;
using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Punching;

/// <summary>Ready-to-show texts of the punch screen (§3.7 cases) in the request language.</summary>
public static class PunchTexts
{
    public static string Clock(DateTime utc)
    {
        var l = JordanTime.FromUtc(utc);
        var h = l.Hour % 12 == 0 ? 12 : l.Hour % 12;
        return Lang.IsArabic ? $"{h}:{l:mm} {(l.Hour < 12 ? "ص" : "م")}" : $"{h}:{l:mm} {(l.Hour < 12 ? "AM" : "PM")}";
    }

    public static string Duration(int minutes) =>
        Lang.IsArabic ? $"{minutes / 60} س {minutes % 60} د" : $"{minutes / 60} h {minutes % 60} m";

    public static string VerdictMessage(ZoneCheck c, AttendanceSettings s) => c.Verdict switch
    {
        PunchVerdict.Rejected when c.Reason == PunchReason.LowAccuracy =>
            Lang.Pick("الإشارة ضعيفة — اطلع لمكان مفتوح وجرّب", "Weak signal — move to an open area and try again"),
        PunchVerdict.Rejected =>
            Lang.Pick($"إنت على بعد {c.DistanceMeters} م من «{c.ZoneName}»", $"You are {c.DistanceMeters} m from \"{c.ZoneName}\""),
        PunchVerdict.Review when c.Reason == PunchReason.Mocked =>
            Lang.Pick("بينسجّل بانتظار تأكيد المالك (موقع وهمي محتمل)", "Recorded, awaiting the owner's confirmation (possibly a fake location)"),
        PunchVerdict.Review =>
            Lang.Pick("بينسجّل بانتظار تأكيد المالك (الموقع مش دقيق)", "Recorded, awaiting the owner's confirmation (the position is not precise)"),
        _ when c.Reason == PunchReason.NoZone =>
            Lang.Pick("مقبول — ما في منطقة بصمة محدّدة للمحطة", "Accepted — the station has no punch zone yet"),
        _ => Lang.Pick($"داخل «{c.ZoneName}» ({c.DistanceMeters} م)", $"Inside \"{c.ZoneName}\" ({c.DistanceMeters} m)"),
    };
}

public record PunchCommand(string ProviderType, int ProviderId,
    /// <summary>in | out.</summary>
    string Direction, double? Lat, double? Lng, int? AccuracyMeters, bool IsMocked = false, string? DeviceId = null) : IRequest<PunchResult>;

public record PunchResult(int PunchId, string Direction, DateTime At, string Verdict, string? Reason, string? ZoneName, int? DistanceMeters,
    int LateMinutes,
    /// <summary>in | out — the worker's state after this punch.</summary>
    string State, int WorkedMinutes,
    /// <summary>An older day is still open (no "out") — tell the owner (case 12).</summary>
    bool OpenFromYesterday, bool NoZone, string Message);

/// <summary>
/// §3.7 — punch in / out. The server stamps the time, judges the position (§3.6 order), applies the shift rules and
/// keeps the day's totals. One lock per worker so a double tap cannot punch twice. Refused attempts are recorded
/// (no day) and the owner is told.
/// </summary>
public class PunchCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, AttendanceNotifier notifier) : IRequestHandler<PunchCommand, PunchResult>
{
    public async Task<PunchResult> Handle(PunchCommand request, CancellationToken ct)
    {
        var direction = (request.Direction ?? "").Trim().ToLowerInvariant();
        if (direction is not (PunchDirection.In or PunchDirection.Out))
            throw new DataValidationException("Direction", "Direction must be 'in' or 'out'");
        if (request.Lat is null || request.Lng is null || request.AccuracyMeters is null)
            throw new DataValidationException("Lat", Lang.Pick("لازم إذن الموقع لتسجيل الحضور", "Location is required to punch"));
        if (request.Lat is < -90 or > 90 || request.Lng is < -180 or > 180 || request.AccuracyMeters < 0)
            throw new DataValidationException("Lat", Lang.Pick("الموقع غير صحيح", "Invalid position"));

        var worker = await AttendanceAccess.EnsureWorkerAsync(db, currentUser, request.ProviderType, request.ProviderId, WorkerPrivileges.Attendance, ct);
        var pm = worker.Assignment;
        var settings = await AttendanceSettings.ReadAsync(db, ct);
        var zones = await ZoneRules.ForWorkerAsync(db, request.ProviderType, request.ProviderId, pm.Id, ct);
        var schedules = await ShiftResolver.LoadAsync(db, pm.Id, ct);

        int? attemptToNotify = null;
        PunchResult result;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // One punch at a time per worker.
            await db.ProviderManagers.FromSqlRaw("SELECT * FROM [ProviderManager] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", pm.Id).AsNoTracking().FirstAsync(ct);

            var now = DateTime.UtcNow;
            var openDay = await db.AttendanceDays.Include(d => d.Punches)
                .Where(d => d.ProviderManagerId == pm.Id && d.IsOpen && !d.IsDeleted)
                .OrderByDescending(d => d.WorkDate).FirstOrDefaultAsync(ct);
            var (workDate, shift) = ShiftResolver.ResolveForIn(schedules, now, settings);

            if (direction == PunchDirection.In)
            {
                if (openDay is not null && openDay.WorkDate == workDate)
                    throw new CodedHttpException(409, AttendanceCodes.AlreadyIn,
                        Lang.Pick($"إنت مسجّل حضور من {PunchTexts.Clock(openDay.FirstInAt ?? now)}", $"You are already punched in since {PunchTexts.Clock(openDay.FirstInAt ?? now)}"),
                        new Dictionary<string, object?> { ["since"] = JordanTime.FromUtc(openDay.FirstInAt ?? now) });

                if (shift is not null && now < shift.StartUtc.AddMinutes(-shift.EarliestPunchMinutes))
                {
                    var earliest = shift.StartUtc.AddMinutes(-shift.EarliestPunchMinutes);
                    throw new CodedHttpException(422, AttendanceCodes.TooEarly,
                        Lang.Pick($"ورديتك بتبلّش {PunchTexts.Clock(shift.StartUtc)} — بتقدر تسجّل من {PunchTexts.Clock(earliest)}",
                                  $"Your shift starts at {PunchTexts.Clock(shift.StartUtc)} — you can punch in from {PunchTexts.Clock(earliest)}"),
                        new Dictionary<string, object?> { ["shiftStart"] = JordanTime.FromUtc(shift.StartUtc), ["earliestAt"] = JordanTime.FromUtc(earliest) });
                }
            }
            else if (openDay is null)
            {
                throw new CodedHttpException(409, AttendanceCodes.NotIn, Lang.Pick("ما سجّلت حضور بعد", "You have not punched in yet"));
            }

            var check = ZoneRules.Evaluate(zones, request.Lat.Value, request.Lng.Value, request.AccuracyMeters.Value, request.IsMocked, settings);
            var punch = new Punch
            {
                ProviderManagerId = pm.Id, WorkDate = direction == PunchDirection.In ? workDate : openDay!.WorkDate, Direction = direction, At = now,
                Lat = request.Lat, Lng = request.Lng, AccuracyMeters = request.AccuracyMeters, IsMocked = request.IsMocked,
                DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId.Trim(),
                PunchZoneId = check.ZoneId, DistanceMeters = check.DistanceMeters, Verdict = check.Verdict, Reason = check.Reason, Source = PunchSource.App,
            };

            if (check.Verdict == PunchVerdict.Rejected)
            {
                db.Punches.Add(punch);
                await db.SaveChanges(ct);
                await tx.CommitAsync(ct);
                await notifier.NotifyAttemptAsync(punch.Id, ct);
                var code = check.Reason == PunchReason.LowAccuracy ? AttendanceCodes.LowAccuracy : AttendanceCodes.OutsideZone;
                throw new CodedHttpException(422, code, PunchTexts.VerdictMessage(check, settings), new Dictionary<string, object?>
                {
                    ["distanceMeters"] = check.DistanceMeters, ["zoneName"] = check.ZoneName, ["radiusMeters"] = check.RadiusMeters, ["accuracyMeters"] = request.AccuracyMeters,
                });
            }

            AttendanceDay day;
            var lateMinutes = 0;
            if (direction == PunchDirection.In)
            {
                day = await db.AttendanceDays.Include(d => d.Punches)
                          .FirstOrDefaultAsync(d => d.ProviderManagerId == pm.Id && d.WorkDate == workDate && !d.IsDeleted, ct)
                      ?? NewDay(pm.Id, workDate, shift);
                if (day.Id == 0) db.AttendanceDays.Add(day);
                if (day.FirstInAt is null)
                {
                    day.FirstInAt = now;
                    if (shift is not null && now > shift.StartUtc.AddMinutes(shift.GraceMinutes))
                    {
                        day.LateMinutes = (int)Math.Round((now - shift.StartUtc).TotalMinutes);
                        day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Late);
                    }
                    if (shift is null) day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Extra);
                    if (await AttendanceAccess.IsHolidayAsync(db, workDate, ct)) { day.Status = AttendanceStatus.Holiday; day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Extra); }
                }
                if (day.Status == AttendanceStatus.Leave) day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Extra);   // case 13: worked on a leave day
                if (day.Status is not AttendanceStatus.Holiday) day.Status = AttendanceStatus.Present;
                day.IsOpen = true;
                lateMinutes = day.LateMinutes;
            }
            else
            {
                day = openDay!;
                var lastIn = day.Punches.Where(p => p.Direction == PunchDirection.In && p.Verdict != PunchVerdict.Rejected && !p.IsDeleted).OrderByDescending(p => p.At).First();
                var minutes = Math.Max(0, (int)Math.Round((now - lastIn.At).TotalMinutes));
                day.MinutesWorked += minutes;
                if (day.ShiftStartUtc is null || day.Status == AttendanceStatus.Holiday) day.ExtraMinutes += minutes;
                day.LastOutAt = now;
                day.IsOpen = false;
                lateMinutes = day.LateMinutes;
            }
            if (check.Verdict == PunchVerdict.Review) day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Review);
            if (check.Reason == PunchReason.NoZone) day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.NoZone);

            day.Punches.Add(punch);
            await db.SaveChanges(ct);
            await tx.CommitAsync(ct);
            if (check.Verdict == PunchVerdict.Review) attemptToNotify = punch.Id;

            var openFromYesterday = direction == PunchDirection.In && openDay is not null && openDay.WorkDate != workDate;
            var worked = direction == PunchDirection.Out ? day.MinutesWorked : day.MinutesWorked;
            var message = direction == PunchDirection.In
                ? Lang.Pick($"✔ سجّلت حضورك {PunchTexts.Clock(now)}", $"✔ Punched in at {PunchTexts.Clock(now)}")
                  + (lateMinutes > 0 ? Lang.Pick($" · متأخر {lateMinutes} د", $" · {lateMinutes} min late") : "")
                : Lang.Pick($"✔ سجّلت انصرافك {PunchTexts.Clock(now)} · {PunchTexts.Duration(day.MinutesWorked)}", $"✔ Punched out at {PunchTexts.Clock(now)} · {PunchTexts.Duration(day.MinutesWorked)}");
            if (check.Verdict == PunchVerdict.Review || check.Reason == PunchReason.NoZone) message += " — " + PunchTexts.VerdictMessage(check, settings);
            if (openFromYesterday) message += Lang.Pick(" · آخر دوام ما إله انصراف — بلّغ المالك", " · your last shift has no punch-out — tell the owner");

            result = new PunchResult(punch.Id, direction, now, check.Verdict, check.Reason, check.ZoneName, check.DistanceMeters, lateMinutes,
                direction, worked, openFromYesterday, check.Reason == PunchReason.NoZone, message);
        }

        if (attemptToNotify is int id) await notifier.NotifyAttemptAsync(id, ct);
        return result;
    }

    private static AttendanceDay NewDay(int providerManagerId, DateOnly workDate, ShiftWindow? shift) => new()
    {
        ProviderManagerId = providerManagerId, WorkDate = workDate, ShiftStartUtc = shift?.StartUtc, ShiftEndUtc = shift?.EndUtc, Status = AttendanceStatus.Present,
    };
}

public record AttendanceDayBriefDto(DateOnly WorkDate, string Status, string? Flags, int MinutesWorked, int LateMinutes, DateTime? FirstInAt, DateTime? LastOutAt);

public record AttendanceTodayDto(
    /// <summary>in | out | finished (punched out today; may punch in again) | leave (approved leave today; a punch still works and counts as extra) | off (no shift today, no punch yet).</summary>
    string State, DateOnly WorkDate, DateTime? PunchedInAt, DateTime? PunchedOutAt, ShiftWindowDto? Shift, int LateMinutes,
    /// <summary>Minutes worked today, live while punched in.</summary>
    int WorkedMinutes, bool OpenFromYesterday, bool HasZone, bool IsHoliday,
    /// <summary>When an "in" becomes possible (null = now).</summary>
    DateTime? EarliestAt, bool CanPunchIn, bool CanPunchOut, string Hint, List<AttendanceDayBriefDto> Last7Days);

/// <summary>The worker's "my shift" screen (§3.7).</summary>
public record GetMyAttendanceTodayRequest(string ProviderType, int ProviderId) : IRequest<AttendanceTodayDto>;

public class GetMyAttendanceTodayRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetMyAttendanceTodayRequest, AttendanceTodayDto>
{
    public async Task<AttendanceTodayDto> Handle(GetMyAttendanceTodayRequest request, CancellationToken ct)
    {
        var worker = await AttendanceAccess.EnsureWorkerAsync(db, currentUser, request.ProviderType, request.ProviderId, WorkerPrivileges.Attendance, ct);
        var pm = worker.Assignment;
        var settings = await AttendanceSettings.ReadAsync(db, ct);
        var schedules = await ShiftResolver.LoadAsync(db, pm.Id, ct);
        var now = DateTime.UtcNow;
        var (workDate, shift) = ShiftResolver.ResolveForIn(schedules, now, settings);

        var openDay = await db.AttendanceDays.AsNoTracking().Include(d => d.Punches)
            .Where(d => d.ProviderManagerId == pm.Id && d.IsOpen && !d.IsDeleted).OrderByDescending(d => d.WorkDate).FirstOrDefaultAsync(ct);
        var today = openDay?.WorkDate == workDate ? openDay
            : await db.AttendanceDays.AsNoTracking().Include(d => d.Punches).FirstOrDefaultAsync(d => d.ProviderManagerId == pm.Id && d.WorkDate == workDate && !d.IsDeleted, ct);

        var worked = today?.MinutesWorked ?? 0;
        if (today is { IsOpen: true })
        {
            var lastIn = today.Punches.Where(p => p.Direction == PunchDirection.In && p.Verdict != PunchVerdict.Rejected && !p.IsDeleted).OrderByDescending(p => p.At).FirstOrDefault();
            if (lastIn is not null) worked += Math.Max(0, (int)Math.Round((now - lastIn.At).TotalMinutes));
        }

        var isLeave = today is { Status: AttendanceStatus.Leave, FirstInAt: null };
        var state = today is { IsOpen: true } ? "in" : today?.LastOutAt is not null ? "finished" : isLeave ? "leave" : shift is null ? "off" : "out";
        var earliest = shift is not null && now < shift.StartUtc.AddMinutes(-shift.EarliestPunchMinutes) ? shift.StartUtc.AddMinutes(-shift.EarliestPunchMinutes) : (DateTime?)null;
        var canIn = state != "in" && earliest is null;
        var hasZone = await db.PunchZones.AsNoTracking().AnyAsync(z => z.ProviderType == request.ProviderType && z.ProviderId == request.ProviderId && z.IsActive && !z.IsDeleted
                                                                      && z.Workers.Any(w => w.ProviderManagerId == pm.Id && !w.IsDeleted), ct);
        var isHoliday = await AttendanceAccess.IsHolidayAsync(db, workDate, ct);
        var openFromYesterday = openDay is not null && openDay.WorkDate != workDate;

        var hint = state == "in" ? Lang.Pick($"موجود من {PunchTexts.Clock(today!.FirstInAt ?? now)} · {PunchTexts.Duration(worked)}", $"In since {PunchTexts.Clock(today!.FirstInAt ?? now)} · {PunchTexts.Duration(worked)}")
            : earliest is DateTime e ? Lang.Pick($"ورديتك بتبلّش {PunchTexts.Clock(shift!.StartUtc)} — بتقدر تسجّل من {PunchTexts.Clock(e)}", $"Your shift starts at {PunchTexts.Clock(shift!.StartUtc)} — you can punch in from {PunchTexts.Clock(e)}")
            : state == "finished" ? Lang.Pick($"خلصت دوامك · {PunchTexts.Duration(worked)}", $"Shift finished · {PunchTexts.Duration(worked)}")
            : isHoliday ? Lang.Pick("اليوم عطلة رسمية — الشغل فيها بينحسب إضافي", "Today is a public holiday — work counts as extra")
            : shift is null ? Lang.Pick("ما عندك وردية اليوم — البصمة بتنحسب دوام إضافي", "No shift today — a punch counts as extra work")
            : Lang.Pick($"سجّل حضور (ورديتك {PunchTexts.Clock(shift.StartUtc)})", $"Punch in (your shift starts {PunchTexts.Clock(shift.StartUtc)})");
        if (isLeave) hint = Lang.Pick("اليوم عندك إجازة — بدّك تسجّل دوام؟", "You are on leave today — record work anyway?");
        if (openFromYesterday) hint += Lang.Pick(" · آخر دوام ما إله انصراف — بلّغ المالك", " · your last shift has no punch-out — tell the owner");

        var since = workDate.AddDays(-7);
        var last7 = await db.AttendanceDays.AsNoTracking()
            .Where(d => d.ProviderManagerId == pm.Id && !d.IsDeleted && d.WorkDate >= since && d.WorkDate < workDate)
            .OrderByDescending(d => d.WorkDate)
            .Select(d => new AttendanceDayBriefDto(d.WorkDate, d.Status, d.Flags, d.MinutesWorked, d.LateMinutes, d.FirstInAt, d.LastOutAt))
            .ToListAsync(ct);

        return new AttendanceTodayDto(state, workDate, today?.FirstInAt, today?.LastOutAt, ShiftWindowDto.From(shift), today?.LateMinutes ?? 0, worked,
            openFromYesterday, hasZone, isHoliday, earliest, canIn, state == "in", hint, last7);
    }
}

public record WhoIsInRowDto(int ProviderManagerId, string? Name,
    /// <summary>in | late (in, after the grace) | notPunched (shift started, nothing yet) | out (punched out) | leave | off (no shift today) | holiday | beforeShift.</summary>
    string State, DateTime? Since, int LateMinutes, string? ZoneName, string? ShiftText, DateTime? ShiftStartAt, int WorkedMinutes, bool Review);

/// <summary>"Who is in now" (§3.8): owner, admin or ViewAttendance.</summary>
public record GetWhoIsInRequest(string ProviderType, int ProviderId) : IRequest<List<WhoIsInRowDto>>;

public class GetWhoIsInRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetWhoIsInRequest, List<WhoIsInRowDto>>
{
    public async Task<List<WhoIsInRowDto>> Handle(GetWhoIsInRequest request, CancellationToken ct)
    {
        await AttendanceAccess.EnsureCanViewAsync(db, currentUser, request.ProviderType, request.ProviderId, ct);
        var settings = await AttendanceSettings.ReadAsync(db, ct);
        var now = DateTime.UtcNow;
        var todayLocal = DateOnly.FromDateTime(JordanTime.FromUtc(now));
        var isHoliday = await AttendanceAccess.IsHolidayAsync(db, todayLocal, ct);

        var workers = await db.ProviderManagers.AsNoTracking()
            .Where(pm => pm.ProviderType == request.ProviderType && pm.ProviderId == request.ProviderId && pm.IsActive && pm.SuspendedByPlanAt == null && !pm.IsDeleted)
            .OrderBy(pm => pm.CreatedAt)
            .Select(pm => new { pm.Id, pm.User.Name })
            .ToListAsync(ct);
        var ids = workers.Select(w => w.Id).ToList();

        var schedules = await db.WorkerShiftSchedules.AsNoTracking().Include(s => s.Days)
            .Where(s => ids.Contains(s.ProviderManagerId) && !s.IsDeleted).ToListAsync(ct);
        var days = await db.AttendanceDays.AsNoTracking().Include(d => d.Punches)
            .Where(d => ids.Contains(d.ProviderManagerId) && !d.IsDeleted && d.WorkDate >= todayLocal.AddDays(-1))
            .ToListAsync(ct);
        var zoneNames = await db.PunchZones.AsNoTracking()
            .Where(z => z.ProviderType == request.ProviderType && z.ProviderId == request.ProviderId).ToDictionaryAsync(z => z.Id, z => z.Name, ct);

        var rows = new List<WhoIsInRowDto>();
        foreach (var w in workers)
        {
            var mine = schedules.Where(s => s.ProviderManagerId == w.Id).ToList();
            var (workDate, shift) = ShiftResolver.ResolveForIn(mine, now, settings);
            var day = days.Where(d => d.ProviderManagerId == w.Id && d.IsOpen).OrderByDescending(d => d.WorkDate).FirstOrDefault()
                      ?? days.FirstOrDefault(d => d.ProviderManagerId == w.Id && d.WorkDate == workDate);

            var worked = day?.MinutesWorked ?? 0;
            string? zoneName = null;
            var review = day is not null && AttendanceFlags.Parse(day.Flags).Contains(AttendanceFlag.Review);
            if (day is { IsOpen: true })
            {
                var lastIn = day.Punches.Where(p => p.Direction == PunchDirection.In && p.Verdict != PunchVerdict.Rejected && !p.IsDeleted).OrderByDescending(p => p.At).FirstOrDefault();
                if (lastIn is not null) { worked += Math.Max(0, (int)Math.Round((now - lastIn.At).TotalMinutes)); if (lastIn.PunchZoneId is int z) zoneName = zoneNames.GetValueOrDefault(z); }
            }

            var state = day is { IsOpen: true } ? (day.LateMinutes > 0 ? "late" : "in")
                : day?.LastOutAt is not null ? "out"
                : day is { Status: AttendanceStatus.Leave } ? "leave"
                : isHoliday ? "holiday"
                : shift is null ? "off"
                : now < shift.StartUtc.AddMinutes(shift.GraceMinutes) ? "beforeShift"
                : "notPunched";

            rows.Add(new WhoIsInRowDto(w.Id, w.Name, state, day?.FirstInAt, day?.LateMinutes ?? 0, zoneName, shift?.Text(null), shift?.StartUtc, worked, review));
        }
        return rows;
    }
}
