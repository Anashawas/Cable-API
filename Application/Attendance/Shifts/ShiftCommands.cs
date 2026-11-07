using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Shifts;

public record ShiftDayDto(
    /// <summary>0 = Sunday … 6 = Saturday.</summary>
    int DayOfWeek,
    /// <summary>"06:00"; null on an off day.</summary>
    string? StartTime,
    /// <summary>"14:00"; "06:00" with a 22:00 start = crosses midnight.</summary>
    string? EndTime,
    bool Off);

public record ShiftScheduleDto(int Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, int GraceMinutes, int EarliestPunchMinutes, List<ShiftDayDto> Days);

public record ShiftWindowDto(DateOnly WorkDate, DateTime StartAt, DateTime EndAt, string Text, bool CrossesMidnight)
{
    public static ShiftWindowDto? From(ShiftWindow? w) =>
        w is null ? null : new ShiftWindowDto(w.WorkDate, w.StartUtc, w.EndUtc, w.Text(null), w.CrossesMidnight);
}

public record WorkerShiftsDto(int ProviderManagerId, ShiftScheduleDto? Current, ShiftScheduleDto? Upcoming, ShiftWindowDto? Today);

internal static class ShiftDtos
{
    public static ShiftScheduleDto ToDto(WorkerShiftSchedule s) => new(s.Id, s.EffectiveFrom, s.EffectiveTo, s.GraceMinutes, s.EarliestPunchMinutes,
        Enumerable.Range(0, 7).Select(d =>
        {
            var day = s.Days.FirstOrDefault(x => x.DayOfWeek == d && !x.IsDeleted);
            return day is null || day.IsOff || day.StartTime is null
                ? new ShiftDayDto(d, null, null, true)
                : new ShiftDayDto(d, day.StartTime.Value.ToString("HH:mm"), day.EndTime!.Value.ToString("HH:mm"), false);
        }).ToList());
}

/// <summary>The worker's schedule: owner, admin, or the worker themself.</summary>
public record GetWorkerShiftsRequest(int ProviderManagerId) : IRequest<WorkerShiftsDto>;

public class GetWorkerShiftsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetWorkerShiftsRequest, WorkerShiftsDto>
{
    public async Task<WorkerShiftsDto> Handle(GetWorkerShiftsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var pm = await db.ProviderManagers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ProviderManagerId && !x.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");
        if (pm.UserId != userId)
            await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, pm.ProviderType, pm.ProviderId, cancellationToken);

        var schedules = await ShiftResolver.LoadAsync(db, pm.Id, cancellationToken);
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var current = ShiftResolver.ScheduleFor(schedules, today);
        var upcoming = schedules.Where(s => s.EffectiveFrom > today).OrderBy(s => s.EffectiveFrom).FirstOrDefault();
        return new WorkerShiftsDto(pm.Id, current is null ? null : ShiftDtos.ToDto(current), upcoming is null ? null : ShiftDtos.ToDto(upcoming),
            ShiftWindowDto.From(ShiftResolver.ForDate(schedules, today)));
    }
}

/// <summary>
/// Owner sets the weekly schedule (§3.5). Applies from <paramref name="EffectiveFrom"/> (default: today for a worker
/// with no schedule, else next Sunday); the running schedule ends the day before, later ones are replaced. Past days
/// never change. Missing days = off.
/// </summary>
public record SetWorkerShiftsCommand(int ProviderManagerId, DateOnly? EffectiveFrom, List<ShiftDayDto> Days, int? GraceMinutes = null, int? EarliestPunchMinutes = null)
    : IRequest<WorkerShiftsDto>;

public class SetWorkerShiftsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SetWorkerShiftsCommand, WorkerShiftsDto>
{
    public async Task<WorkerShiftsDto> Handle(SetWorkerShiftsCommand request, CancellationToken cancellationToken)
    {
        var pm = await db.ProviderManagers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ProviderManagerId && !x.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, pm.ProviderType, pm.ProviderId, cancellationToken);
        await AttendanceAccess.EnsurePlanAsync(db, pm.ProviderType, pm.ProviderId, cancellationToken);

        var settings = await AttendanceSettings.ReadAsync(db, cancellationToken);
        var days = ParseDays(request.Days ?? []);
        var grace = request.GraceMinutes ?? settings.DefaultGraceMinutes;
        var earliest = request.EarliestPunchMinutes ?? settings.DefaultEarliestPunchMinutes;
        if (grace is < 0 or > 180) throw new DataValidationException("GraceMinutes", Lang.Pick("سماحية التأخير بين 0 و180 دقيقة", "Grace must be between 0 and 180 minutes"));
        if (earliest is < 0 or > 720) throw new DataValidationException("EarliestPunchMinutes", Lang.Pick("أبكر بصمة بين 0 و720 دقيقة", "Earliest punch must be between 0 and 720 minutes"));

        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var existing = await db.WorkerShiftSchedules.Include(s => s.Days)
            .Where(s => s.ProviderManagerId == pm.Id && !s.IsDeleted).OrderBy(s => s.EffectiveFrom).ToListAsync(cancellationToken);

        var from = request.EffectiveFrom ?? (existing.Count == 0 ? today : NextSunday(today));
        if (from < today) throw new DataValidationException("EffectiveFrom", Lang.Pick("التاريخ لازم يكون اليوم أو بعده — الأيام اللي فاتت ما بتتغيّر", "The date must be today or later — past days never change"));

        // Later versions are replaced; the one starting the same day is rewritten; the running one ends the day before.
        foreach (var s in existing.Where(s => s.EffectiveFrom > from)) s.IsDeleted = true;
        var same = existing.FirstOrDefault(s => s.EffectiveFrom == from);
        foreach (var s in existing.Where(s => s.EffectiveFrom < from && (s.EffectiveTo == null || s.EffectiveTo >= from)))
            s.EffectiveTo = from.AddDays(-1);

        var schedule = same ?? new WorkerShiftSchedule { ProviderManagerId = pm.Id, EffectiveFrom = from };
        if (same is null) db.WorkerShiftSchedules.Add(schedule);
        schedule.EffectiveTo = null;
        schedule.GraceMinutes = grace;
        schedule.EarliestPunchMinutes = earliest;
        foreach (var d in schedule.Days) d.IsDeleted = true;
        foreach (var (dow, start, end) in days)
            schedule.Days.Add(new WorkerShiftDay { DayOfWeek = dow, StartTime = start, EndTime = end, IsOff = start is null });
        await db.SaveChanges(cancellationToken);

        var schedules = await ShiftResolver.LoadAsync(db, pm.Id, cancellationToken);
        var current = ShiftResolver.ScheduleFor(schedules, today);
        var upcoming = schedules.Where(s => s.EffectiveFrom > today).OrderBy(s => s.EffectiveFrom).FirstOrDefault();
        return new WorkerShiftsDto(pm.Id, current is null ? null : ShiftDtos.ToDto(current), upcoming is null ? null : ShiftDtos.ToDto(upcoming),
            ShiftWindowDto.From(ShiftResolver.ForDate(schedules, today)));
    }

    private static DateOnly NextSunday(DateOnly today)
    {
        var days = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

    private static List<(int Dow, TimeOnly? Start, TimeOnly? End)> ParseDays(List<ShiftDayDto> input)
    {
        var result = new List<(int, TimeOnly?, TimeOnly?)>();
        var seen = new HashSet<int>();
        foreach (var d in input)
        {
            if (d.DayOfWeek is < 0 or > 6) throw new DataValidationException("Days", Lang.Pick("اليوم لازم يكون بين 0 و6", "dayOfWeek must be 0–6"));
            if (!seen.Add(d.DayOfWeek)) throw new DataValidationException("Days", Lang.Pick("يوم مكرّر", "Duplicate day"));
            if (d.Off || (string.IsNullOrWhiteSpace(d.StartTime) && string.IsNullOrWhiteSpace(d.EndTime))) { result.Add((d.DayOfWeek, null, null)); continue; }
            if (!TimeOnly.TryParseExact(d.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(d.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                throw new DataValidationException("Days", Lang.Pick("الوقت بصيغة HH:mm", "Times must be HH:mm"));
            result.Add((d.DayOfWeek, start, end));
        }
        for (var dow = 0; dow < 7; dow++) if (!seen.Contains(dow)) result.Add((dow, null, null));
        return result;
    }
}
