using System.Text;
using Application.Attendance.Punching;
using Application.Common.Interfaces;
using Application.Common.Utilities;
using Application.Ocpp.LiveCharging;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Reports;

/// <summary>Recomputes a day's totals from its punches (after a punch, a manual entry or a correction).</summary>
public static class AttendanceMath
{
    public static void Recompute(AttendanceDay day, int graceMinutes)
    {
        var punches = day.Punches.Where(p => !p.IsDeleted && p.Verdict != PunchVerdict.Rejected).OrderBy(p => p.At).ThenBy(p => p.Id).ToList();
        day.FirstInAt = punches.FirstOrDefault(p => p.Direction == PunchDirection.In)?.At;
        day.LastOutAt = punches.LastOrDefault(p => p.Direction == PunchDirection.Out)?.At;

        var minutes = 0; DateTime? openIn = null;
        foreach (var p in punches)
        {
            if (p.Direction == PunchDirection.In) openIn ??= p.At;
            else if (openIn is DateTime i) { minutes += Math.Max(0, (int)Math.Round((p.At - i).TotalMinutes)); openIn = null; }
        }
        day.IsOpen = openIn is not null;
        day.MinutesWorked = minutes;

        var flags = AttendanceFlags.Parse(day.Flags);
        day.LateMinutes = 0; flags.Remove(AttendanceFlag.Late);
        if (day.ShiftStartUtc is DateTime start && day.FirstInAt is DateTime first && first > start.AddMinutes(graceMinutes))
        {
            day.LateMinutes = (int)Math.Round((first - start).TotalMinutes);
            flags.Add(AttendanceFlag.Late);
        }
        if (day.ShiftStartUtc is null || day.Status == AttendanceStatus.Holiday) { day.ExtraMinutes = minutes; flags.Add(AttendanceFlag.Extra); }
        else day.ExtraMinutes = Math.Max(0, minutes - (int)Math.Round((day.ShiftEndUtc!.Value - day.ShiftStartUtc.Value).TotalMinutes));
        if (day.FirstInAt is not null && day.Status is AttendanceStatus.Absent or AttendanceStatus.Off) day.Status = AttendanceStatus.Present;
        day.Flags = flags.Count == 0 ? null : string.Join(",", flags);
    }
}

public record AttendanceDayRowDto(int? Id, int ProviderManagerId, string? WorkerName, DateOnly WorkDate,
    /// <summary>present | missingOut | absent | leave | holiday | off.</summary>
    string Status, List<string> Flags, DateTime? FirstInAt, DateTime? LastOutAt, int MinutesWorked, int LateMinutes, int ExtraMinutes,
    string? ShiftText, string? ZoneName,
    /// <summary>app | manual | correction | — ("طريقة التسجيل").</summary>
    string Method, string? RecordedByName, bool IsOpen, int Punches);

public record WorkerSummaryDto(int ProviderManagerId, string? WorkerName, int DaysPresent, int MinutesWorked, int LateMinutes, int DaysLate,
    int DaysAbsent, int DaysLeave, int DaysMissingOut, int ExtraMinutes, int DaysReview);

public record AttendanceTableDto(DateOnly From, DateOnly To, List<AttendanceDayRowDto> Days, List<WorkerSummaryDto> Summary);

/// <summary>Builds the attendance table for a provider: stored days plus synthesised absent / off / holiday days.</summary>
public static class AttendanceTable
{
    public const int MaxDays = 92;

    public static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var f = from ?? new DateOnly(today.Year, today.Month, 1);
        var t = to ?? today;
        if (t < f) throw new DataValidationException("To", "to must be after from");
        if (t.DayNumber - f.DayNumber > MaxDays) throw new DataValidationException("To", $"The range cannot exceed {MaxDays} days");
        return (f, t);
    }

    public static async Task<AttendanceTableDto> BuildAsync(IApplicationDbContext db, string providerType, int providerId, int? workerId,
        DateOnly from, DateOnly to, string? statusFilter, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var workers = await db.ProviderManagers.AsNoTracking()
            .Where(pm => pm.ProviderType == providerType && pm.ProviderId == providerId && !pm.IsDeleted && (workerId == null || pm.Id == workerId))
            .OrderBy(pm => pm.CreatedAt)
            .Select(pm => new { pm.Id, pm.User.Name, pm.Privileges, pm.CreatedAt })
            .ToListAsync(ct);
        var ids = workers.Select(w => w.Id).ToList();

        var days = await db.AttendanceDays.AsNoTracking().Include(d => d.Punches)
            .Where(d => ids.Contains(d.ProviderManagerId) && !d.IsDeleted && d.WorkDate >= from && d.WorkDate <= to).ToListAsync(ct);
        var schedules = await db.WorkerShiftSchedules.AsNoTracking().Include(s => s.Days)
            .Where(s => ids.Contains(s.ProviderManagerId) && !s.IsDeleted).ToListAsync(ct);
        var holidays = (await db.PublicHolidays.AsNoTracking().Where(h => !h.IsDeleted && h.Date >= from && h.Date <= to).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var zoneNames = await db.PunchZones.AsNoTracking().Where(z => z.ProviderType == providerType && z.ProviderId == providerId).ToDictionaryAsync(z => z.Id, z => z.Name, ct);
        var recorderIds = days.SelectMany(d => d.Punches).Where(p => p.RecordedByUserId != null).Select(p => p.RecordedByUserId!.Value).Distinct().ToList();
        var recorders = recorderIds.Count == 0 ? new Dictionary<int, string?>() : await db.UserAccounts.AsNoTracking().Where(u => recorderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var rows = new List<AttendanceDayRowDto>();
        foreach (var w in workers)
        {
            var mine = schedules.Where(s => s.ProviderManagerId == w.Id).ToList();
            var tracks = WorkerPrivileges.Has(w.Privileges, WorkerPrivileges.Attendance);
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var day = days.FirstOrDefault(d => d.ProviderManagerId == w.Id && d.WorkDate == date);
                if (day is not null) { rows.Add(Row(day, w.Id, w.Name, zoneNames, recorders)); continue; }

                // Nothing recorded: what the day was.
                if (date < DateOnly.FromDateTime(w.CreatedAt)) continue;
                var shift = ShiftResolver.ForDate(mine, date);
                if (holidays.Contains(date)) { rows.Add(Empty(w.Id, w.Name, date, AttendanceStatus.Holiday, shift)); continue; }
                if (shift is null) { if (ShiftResolver.ScheduleFor(mine, date) is not null) rows.Add(Empty(w.Id, w.Name, date, AttendanceStatus.Off, null)); continue; }
                if (tracks && shift.EndUtc <= now) rows.Add(Empty(w.Id, w.Name, date, AttendanceStatus.Absent, shift));
            }
        }
        if (!string.IsNullOrWhiteSpace(statusFilter)) rows = rows.Where(r => string.Equals(r.Status, statusFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        rows = rows.OrderByDescending(r => r.WorkDate).ThenBy(r => r.WorkerName).ToList();

        var summary = workers.Select(w =>
        {
            var mine = rows.Where(r => r.ProviderManagerId == w.Id).ToList();
            return new WorkerSummaryDto(w.Id, w.Name,
                mine.Count(r => r.Status is AttendanceStatus.Present or AttendanceStatus.MissingOut || (r.Status == AttendanceStatus.Holiday && r.FirstInAt != null)),
                mine.Sum(r => r.MinutesWorked), mine.Sum(r => r.LateMinutes), mine.Count(r => r.LateMinutes > 0),
                mine.Count(r => r.Status == AttendanceStatus.Absent), mine.Count(r => r.Status == AttendanceStatus.Leave),
                mine.Count(r => r.Status == AttendanceStatus.MissingOut), mine.Sum(r => r.ExtraMinutes), mine.Count(r => r.Flags.Contains(AttendanceFlag.Review)));
        }).ToList();

        return new AttendanceTableDto(from, to, rows, summary);
    }

    public static AttendanceDayRowDto Row(AttendanceDay day, int pmId, string? name, Dictionary<int, string> zoneNames, Dictionary<int, string?> recorders)
    {
        var punches = day.Punches.Where(p => !p.IsDeleted && p.Verdict != PunchVerdict.Rejected).OrderBy(p => p.At).ToList();
        var firstIn = punches.FirstOrDefault(p => p.Direction == PunchDirection.In);
        var sources = punches.Select(p => p.Source).Distinct().ToList();
        var method = punches.Count == 0 ? "—" : sources.Contains(PunchSource.Correction) ? PunchSource.Correction : sources.Contains(PunchSource.Manual) ? PunchSource.Manual : PunchSource.App;
        var recorder = punches.Where(p => p.RecordedByUserId != null).Select(p => recorders.GetValueOrDefault(p.RecordedByUserId!.Value)).FirstOrDefault();
        var shiftText = day.ShiftStartUtc is DateTime s && day.ShiftEndUtc is DateTime e ? $"{JordanTime.FromUtc(s):HH:mm} – {JordanTime.FromUtc(e):HH:mm}" : null;
        return new AttendanceDayRowDto(day.Id, pmId, name, day.WorkDate, day.Status, AttendanceFlags.Parse(day.Flags).ToList(), day.FirstInAt, day.LastOutAt,
            day.MinutesWorked, day.LateMinutes, day.ExtraMinutes, shiftText, firstIn?.PunchZoneId is int z ? zoneNames.GetValueOrDefault(z) : null,
            method, recorder, day.IsOpen, punches.Count);
    }

    private static AttendanceDayRowDto Empty(int pmId, string? name, DateOnly date, string status, ShiftWindow? shift) =>
        new(null, pmId, name, date, status, [], null, null, 0, 0, 0, shift?.Text(null), null, "—", null, false, 0);
}

/// <summary>§3.8 — the attendance table and the period summary. Owner, admin or ViewAttendance. Default range: this month.</summary>
public record GetAttendanceTableRequest(string ProviderType, int ProviderId, int? WorkerId, DateOnly? From, DateOnly? To, string? Status) : IRequest<AttendanceTableDto>;

public class GetAttendanceTableRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetAttendanceTableRequest, AttendanceTableDto>
{
    public async Task<AttendanceTableDto> Handle(GetAttendanceTableRequest request, CancellationToken ct)
    {
        await AttendanceAccess.EnsureCanViewAsync(db, currentUser, request.ProviderType, request.ProviderId, ct);
        var (from, to) = AttendanceTable.Range(request.From, request.To);
        return await AttendanceTable.BuildAsync(db, request.ProviderType, request.ProviderId, request.WorkerId, from, to, request.Status, ct);
    }
}

/// <summary>The CSV for payroll (UTF-8 with BOM). Same filters as the table.</summary>
public record GetAttendanceCsvRequest(string ProviderType, int ProviderId, int? WorkerId, DateOnly? From, DateOnly? To, string? Status) : IRequest<LiveChargingFile>;

public class GetAttendanceCsvRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetAttendanceCsvRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetAttendanceCsvRequest request, CancellationToken ct)
    {
        await AttendanceAccess.EnsureCanViewAsync(db, currentUser, request.ProviderType, request.ProviderId, ct);
        var (from, to) = AttendanceTable.Range(request.From, request.To);
        var table = await AttendanceTable.BuildAsync(db, request.ProviderType, request.ProviderId, request.WorkerId, from, to, request.Status, ct);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", Lang.IsArabic
            ? new[] { "الموظف", "التاريخ", "الحالة", "الحضور", "الانصراف", "الساعات", "التأخير (د)", "الإضافي (د)", "المنطقة", "طريقة التسجيل", "سجّله", "العلامات", "الوردية" }
            : new[] { "Worker", "Date", "Status", "In", "Out", "Hours", "Late (min)", "Extra (min)", "Zone", "Method", "Recorded by", "Flags", "Shift" }));
        foreach (var r in table.Days.OrderBy(r => r.WorkerName).ThenBy(r => r.WorkDate))
            csv.AppendLine(string.Join(",", new[]
            {
                CsvList.Cell(r.WorkerName), r.WorkDate.ToString("yyyy-MM-dd"), r.Status,
                r.FirstInAt is DateTime i ? JordanTime.FromUtc(i).ToString("HH:mm") : "", r.LastOutAt is DateTime o ? JordanTime.FromUtc(o).ToString("HH:mm") : "",
                $"{r.MinutesWorked / 60}:{r.MinutesWorked % 60:00}", r.LateMinutes.ToString(), r.ExtraMinutes.ToString(),
                CsvList.Cell(r.ZoneName), r.Method, CsvList.Cell(r.RecordedByName), CsvList.Cell(string.Join(" ", r.Flags)), CsvList.Cell(r.ShiftText),
            }));
        csv.AppendLine();
        csv.AppendLine(string.Join(",", Lang.IsArabic
            ? new[] { "الموظف", "أيام الحضور", "مجموع الساعات", "مجموع التأخير (د)", "أيام التأخير", "الغياب", "الإجازات", "بدون انصراف", "الإضافي (د)", "للمراجعة" }
            : new[] { "Worker", "Days present", "Total hours", "Total late (min)", "Days late", "Absent", "Leave", "Missing out", "Extra (min)", "Review" }));
        foreach (var s in table.Summary)
            csv.AppendLine(string.Join(",", new[] { CsvList.Cell(s.WorkerName), s.DaysPresent.ToString(), $"{s.MinutesWorked / 60}:{s.MinutesWorked % 60:00}", s.LateMinutes.ToString(),
                s.DaysLate.ToString(), s.DaysAbsent.ToString(), s.DaysLeave.ToString(), s.DaysMissingOut.ToString(), s.ExtraMinutes.ToString(), s.DaysReview.ToString() }));

        return new LiveChargingFile(CsvList.Utf8WithBom(csv), "text/csv; charset=utf-8", $"attendance-{request.ProviderType}-{request.ProviderId}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }
}

/// <summary>The worker's own month (§6.1 "my attendance log"). month = YYYY-MM (Jordan), default this month.</summary>
public record GetMyAttendanceHistoryRequest(string ProviderType, int ProviderId, string? Month) : IRequest<AttendanceTableDto>;

public class GetMyAttendanceHistoryRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetMyAttendanceHistoryRequest, AttendanceTableDto>
{
    public async Task<AttendanceTableDto> Handle(GetMyAttendanceHistoryRequest request, CancellationToken ct)
    {
        var worker = await AttendanceAccess.EnsureWorkerAsync(db, currentUser, request.ProviderType, request.ProviderId, WorkerPrivileges.Attendance, ct);
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var first = new DateOnly(today.Year, today.Month, 1);
        if (!string.IsNullOrWhiteSpace(request.Month))
        {
            if (!DateOnly.TryParseExact(request.Month.Trim() + "-01", "yyyy-MM-dd", out first))
                throw new DataValidationException("Month", "month must be YYYY-MM");
        }
        var last = first.AddMonths(1).AddDays(-1);
        if (last > today) last = today;
        if (first > today) return new AttendanceTableDto(first, first.AddMonths(1).AddDays(-1), [], []);
        return await AttendanceTable.BuildAsync(db, request.ProviderType, request.ProviderId, worker.Assignment.Id, first, last, null, ct);
    }
}
