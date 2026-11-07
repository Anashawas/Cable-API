using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Workers;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance;

public static class AttendanceCodes
{
    public const string FeatureNotInPlan = "FEATURE_NOT_IN_PLAN";
    public const string PrivilegeMissing = "PRIVILEGE_MISSING";
    public const string WorkerInactive = "WORKER_INACTIVE";
    public const string LowAccuracy = "LOW_ACCURACY";
    public const string OutsideZone = "OUTSIDE_ZONE";
    public const string TooEarly = "TOO_EARLY";
    public const string AlreadyIn = "ALREADY_IN";
    public const string NotIn = "NOT_IN";
    public const string NoteRequired = "NOTE_REQUIRED";
    public const string CannotRecordSelf = "CANNOT_RECORD_SELF";
}

public static class PunchVerdict { public const string Accepted = "accepted", Review = "review", Rejected = "rejected"; }
public static class PunchReason { public const string LowAccuracy = "lowAccuracy", OutsideZone = "outsideZone", Mocked = "mocked", Borderline = "borderline", NoZone = "noZone"; }
public static class PunchSource { public const string App = "app", Manual = "manual", Correction = "correction"; }
public static class PunchDirection { public const string In = "in", Out = "out"; }
public static class AttendanceStatus { public const string Present = "present", MissingOut = "missingOut", Absent = "absent", Leave = "leave", Holiday = "holiday", Off = "off"; }
public static class AttendanceFlag { public const string Late = "late", Review = "review", NoZone = "noZone", Extra = "extra", Manual = "manual", Corrected = "corrected"; }

/// <summary>The attendance knobs (AppSetting rows, every one optional). Defaults = WORKER_TRACKER §3.6 / §12.</summary>
public record AttendanceSettings(int MaxAccuracyMeters, int MinRadiusMeters, int MaxRadiusMeters, int DefaultGraceMinutes,
    int DefaultEarliestPunchMinutes, int AutoCloseHours)
{
    public const string MaxAccuracyKey = "Attendance.MaxAccuracyMeters";
    public const string MinRadiusKey = "Attendance.MinRadiusMeters";
    public const string MaxRadiusKey = "Attendance.MaxRadiusMeters";
    public const string GraceKey = "Attendance.DefaultGraceMinutes";
    public const string EarliestKey = "Attendance.DefaultEarliestPunchMinutes";
    public const string AutoCloseKey = "Attendance.AutoCloseHours";

    public static readonly AttendanceSettings Defaults = new(150, 50, 800, 10, 60, 2);

    public static async Task<AttendanceSettings> ReadAsync(IApplicationDbContext db, CancellationToken ct)
    {
        string[] keys = [MaxAccuracyKey, MinRadiusKey, MaxRadiusKey, GraceKey, EarliestKey, AutoCloseKey];
        var rows = await db.AppSettings.AsNoTracking().Where(s => keys.Contains(s.Key) && !s.IsDeleted).Select(s => new { s.Key, s.Value }).ToListAsync(ct);
        int Int(string key, int fallback, int min, int max) =>
            int.TryParse(rows.FirstOrDefault(r => r.Key == key)?.Value, out var v) && v >= min && v <= max ? v : fallback;
        return new AttendanceSettings(
            Int(MaxAccuracyKey, Defaults.MaxAccuracyMeters, 10, 5000),
            Int(MinRadiusKey, Defaults.MinRadiusMeters, 10, 5000),
            Int(MaxRadiusKey, Defaults.MaxRadiusMeters, 10, 10000),
            Int(GraceKey, Defaults.DefaultGraceMinutes, 0, 180),
            Int(EarliestKey, Defaults.DefaultEarliestPunchMinutes, 0, 720),
            Int(AutoCloseKey, Defaults.AutoCloseHours, 1, 24));
    }
}

public static class Geo
{
    public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_000;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Rad(double d) => d * Math.PI / 180;
}

/// <summary>The shift that applies to one work date, in UTC.</summary>
public record ShiftWindow(DateOnly WorkDate, DateTime StartUtc, DateTime EndUtc, int GraceMinutes, int EarliestPunchMinutes, bool CrossesMidnight)
{
    public string Text(string? lang)
    {
        var s = JordanTime.FromUtc(StartUtc); var e = JordanTime.FromUtc(EndUtc);
        return $"{s:HH:mm} – {e:HH:mm}";
    }
}

public static class ShiftResolver
{
    public static Task<List<WorkerShiftSchedule>> LoadAsync(IApplicationDbContext db, int providerManagerId, CancellationToken ct) =>
        db.WorkerShiftSchedules.AsNoTracking().Include(s => s.Days)
            .Where(s => s.ProviderManagerId == providerManagerId && !s.IsDeleted)
            .OrderBy(s => s.EffectiveFrom)
            .ToListAsync(ct);

    public static WorkerShiftSchedule? ScheduleFor(IEnumerable<WorkerShiftSchedule> schedules, DateOnly date) =>
        schedules.Where(s => s.EffectiveFrom <= date && (s.EffectiveTo == null || s.EffectiveTo >= date))
            .OrderByDescending(s => s.EffectiveFrom).FirstOrDefault();

    /// <summary>The shift of a work date, or null (off day / no schedule).</summary>
    public static ShiftWindow? ForDate(IEnumerable<WorkerShiftSchedule> schedules, DateOnly workDate)
    {
        var schedule = ScheduleFor(schedules, workDate);
        var day = schedule?.Days.FirstOrDefault(d => d.DayOfWeek == (int)workDate.DayOfWeek && !d.IsDeleted);
        if (schedule is null || day is null || day.IsOff || day.StartTime is null || day.EndTime is null) return null;

        var startLocal = workDate.ToDateTime(day.StartTime.Value);
        var endLocal = workDate.ToDateTime(day.EndTime.Value);
        var crosses = day.EndTime.Value <= day.StartTime.Value;
        if (crosses) endLocal = endLocal.AddDays(1);
        return new ShiftWindow(workDate, JordanTime.ToUtc(startLocal), JordanTime.ToUtc(endLocal), schedule.GraceMinutes, schedule.EarliestPunchMinutes, crosses);
    }

    /// <summary>
    /// Which work date an "in" at this moment belongs to: yesterday's shift when it crosses midnight and is still
    /// running (plus the auto-close window), otherwise today (Jordan).
    /// </summary>
    public static (DateOnly WorkDate, ShiftWindow? Shift) ResolveForIn(IEnumerable<WorkerShiftSchedule> schedules, DateTime nowUtc, AttendanceSettings settings)
    {
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(nowUtc));
        var yesterday = ForDate(schedules, today.AddDays(-1));
        if (yesterday is not null && yesterday.CrossesMidnight && nowUtc < yesterday.EndUtc.AddHours(settings.AutoCloseHours))
            return (today.AddDays(-1), yesterday);
        return (today, ForDate(schedules, today));
    }
}

public record ZoneCandidate(int Id, string Name, double Lat, double Lng, int RadiusMeters);

public record ZoneCheck(string Verdict, string? Reason, int? ZoneId, string? ZoneName, int? DistanceMeters, int? RadiusMeters);

/// <summary>§3.6 — the server's decision for one position, in the document's order; the first rule that applies wins.</summary>
public static class ZoneRules
{
    public static ZoneCheck Evaluate(IReadOnlyList<ZoneCandidate> zones, double lat, double lng, int accuracyMeters, bool isMocked, AttendanceSettings s)
    {
        // 1. the signal is too weak to judge
        if (accuracyMeters > s.MaxAccuracyMeters)
            return new ZoneCheck(PunchVerdict.Rejected, PunchReason.LowAccuracy, null, null, null, null);

        // no zone for this worker: accepted anywhere, flagged (decision 7); a mocked position still goes to review
        if (zones.Count == 0)
            return new ZoneCheck(isMocked ? PunchVerdict.Review : PunchVerdict.Accepted, isMocked ? PunchReason.Mocked : PunchReason.NoZone, null, null, null, null);

        var nearest = zones.Select(z => (Zone: z, Distance: Geo.DistanceMeters(lat, lng, z.Lat, z.Lng)))
            .OrderBy(x => x.Distance - x.Zone.RadiusMeters).First();
        var d = (int)Math.Round(nearest.Distance);
        var r = nearest.Zone.RadiusMeters;

        // 2. clearly outside, even if the position is faked
        if (d - accuracyMeters > r)
            return new ZoneCheck(PunchVerdict.Rejected, PunchReason.OutsideZone, nearest.Zone.Id, nearest.Zone.Name, d, r);
        // 3. a faked position is a deterrent, not proof: the owner decides
        if (isMocked)
            return new ZoneCheck(PunchVerdict.Review, PunchReason.Mocked, nearest.Zone.Id, nearest.Zone.Name, d, r);
        // 4. outside, but could be inside given the accuracy
        if (d > r)
            return new ZoneCheck(PunchVerdict.Review, PunchReason.Borderline, nearest.Zone.Id, nearest.Zone.Name, d, r);
        // 5. inside
        return new ZoneCheck(PunchVerdict.Accepted, null, nearest.Zone.Id, nearest.Zone.Name, d, r);
    }

    public static Task<List<ZoneCandidate>> ForWorkerAsync(IApplicationDbContext db, string providerType, int providerId, int providerManagerId, CancellationToken ct) =>
        db.PunchZones.AsNoTracking()
            .Where(z => z.ProviderType == providerType && z.ProviderId == providerId && z.IsActive && !z.IsDeleted
                        && z.Workers.Any(w => w.ProviderManagerId == providerManagerId && !w.IsDeleted))
            .Select(z => new ZoneCandidate(z.Id, z.Name, z.Lat, z.Lng, z.RadiusMeters))
            .ToListAsync(ct);
}

public static class AttendanceFlags
{
    public static string? Add(string? flags, string flag)
    {
        var set = Parse(flags);
        set.Add(flag);
        return set.Count == 0 ? null : string.Join(",", set);
    }

    public static HashSet<string> Parse(string? flags) =>
        (flags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
}

/// <summary>Who may do what on attendance: plan gate, the worker's own assignment, owner / ViewAttendance / admin views.</summary>
public static class AttendanceAccess
{
    public record WorkerContext(ProviderManager Assignment, StationPlanContext Plan, IReadOnlyList<string> Effective);

    /// <summary>The station must have the "workers" feature (§5.6).</summary>
    public static async Task<StationPlanContext> EnsurePlanAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var context = await WorkerAccess.ContextAsync(db, providerType, providerId, ct);
        if (!context.HasWorkers)
            throw new CodedHttpException(403, AttendanceCodes.FeatureNotInPlan,
                Lang.Pick("الدوام مش ضمن باقة المحطة — بالباقة المميّزة", "Attendance is not in the station's plan — available on Premium"));
        return context;
    }

    /// <summary>The caller's usable assignment at this provider with the given effective privilege.</summary>
    public static async Task<WorkerContext> EnsureWorkerAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId,
        string privilege, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var pm = await db.ProviderManagers.AsNoTracking()
                     .FirstOrDefaultAsync(x => x.UserId == userId && x.ProviderType == providerType && x.ProviderId == providerId && !x.IsDeleted, ct)
                 ?? throw new ForbiddenAccessException("You are not a worker at this provider.");
        if (!WorkerAccess.IsUsable(pm))
            throw new CodedHttpException(403, AttendanceCodes.WorkerInactive, Lang.Pick("أوقف صاحب المحطة حسابك", "The station owner has suspended your account"));

        var plan = await EnsurePlanAsync(db, providerType, providerId, ct);
        var effective = plan.Effective(pm.Privileges);
        if (!effective.Contains(privilege, StringComparer.Ordinal))
            throw new CodedHttpException(403, AttendanceCodes.PrivilegeMissing,
                Lang.Pick("ما عندك صلاحية الدوام — اطلبها من صاحب المحطة", "You do not have the attendance permission — ask the station owner"));
        return new WorkerContext(pm, plan, effective);
    }

    /// <summary>Owner, admin, or a usable worker with ViewAttendance (the "who is in" board, the attempts list).</summary>
    public static async Task EnsureCanViewAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct)) return;
        if (await IsOwnerAsync(db, providerType, providerId, userId, ct)) return;
        await EnsureWorkerAsync(db, currentUser, providerType, providerId, WorkerPrivileges.ViewAttendance, ct);
    }

    /// <summary>Owner or admin (shifts, zones).</summary>
    public static async Task EnsureOwnerOrAdminAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct)) return;
        await Workers.WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, providerType, providerId, userId, ct);
    }

    public static async Task<bool> IsOwnerAsync(IApplicationDbContext db, string providerType, int providerId, int userId, CancellationToken ct) =>
        providerType == Workers.WorkerOwnershipHelper.ChargingPoint
            ? await db.ChargingPoints.AsNoTracking().AnyAsync(c => c.Id == providerId && c.OwnerId == userId && !c.IsDeleted, ct)
            : await db.ServiceProviders.AsNoTracking().AnyAsync(s => s.Id == providerId && s.OwnerId == userId && !s.IsDeleted, ct);

    public static Task<bool> IsHolidayAsync(IApplicationDbContext db, DateOnly date, CancellationToken ct) =>
        db.PublicHolidays.AsNoTracking().AnyAsync(h => h.Date == date && !h.IsDeleted, ct);
}
