using Application.Attendance.Reports;
using Application.Common.Interfaces;
using Application.Workers;
using Cable.Core.Constants;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Attendance.Jobs;

/// <summary>
/// The attendance job (Hangfire, every 5 minutes — WORKER_TRACKER §5.6, §3.10):
///  * closes a forgotten "out" 2 h after the shift end as missingOut (no hours added);
///  * marks absent when the shift ended with no punch (holiday → holiday);
///  * reminds the worker +5 min after the shift start / +15 min after the end;
///  * tells the owner and ViewAttendance workers +15 min after the start, grouped per station;
///  * the owner's daily summary at 21:00 Jordan;
///  * scrubs raw coordinates older than 90 days.
/// Every push is keyed in AttendanceEvent, so nothing is sent twice.
/// </summary>
public interface IAttendanceJobs
{
    Task<int> RunTickAsync(CancellationToken cancellationToken = default);
}

public sealed class AttendanceJobs(IApplicationDbContext db, AttendanceNotifier notifier, ILogger<AttendanceJobs> logger) : IAttendanceJobs
{
    public const string ReminderIn = "reminderIn", ReminderOut = "reminderOut", OwnerMissing = "ownerMissing", DailySummary = "dailySummary";
    private const int ReminderInMinutes = 5, OwnerAlertMinutes = 15, ReminderOutMinutes = 15, NoShiftAutoCloseHours = 16, SummaryHour = 21, ScrubDays = 90;

    public async Task<int> RunTickAsync(CancellationToken ct = default)
    {
        var settings = await AttendanceSettings.ReadAsync(db, ct);
        var now = DateTime.UtcNow;
        var count = 0;
        count += await AutoCloseAsync(now, settings, ct);
        count += await WorkersTickAsync(now, settings, ct);
        count += await ScrubAsync(now, ct);
        logger.LogInformation("Attendance tick: {Count} action(s)", count);
        return count;
    }

    private async Task<int> AutoCloseAsync(DateTime now, AttendanceSettings s, CancellationToken ct)
    {
        var limitNoShift = now.AddHours(-NoShiftAutoCloseHours);
        var open = await db.AttendanceDays
            .Where(d => d.IsOpen && !d.IsDeleted && ((d.ShiftEndUtc != null && d.ShiftEndUtc < now.AddHours(-s.AutoCloseHours)) || (d.ShiftEndUtc == null && d.FirstInAt < limitNoShift)))
            .ToListAsync(ct);
        foreach (var d in open)
        {
            d.IsOpen = false;
            d.Status = AttendanceStatus.MissingOut;
            d.AutoClosedAt = now;
        }
        if (open.Count > 0) await db.SaveChanges(ct);
        return open.Count;
    }

    private async Task<int> WorkersTickAsync(DateTime now, AttendanceSettings s, CancellationToken ct)
    {
        var todayLocal = DateOnly.FromDateTime(JordanTime.FromUtc(now));
        var workers = await db.ProviderManagers.AsNoTracking()
            .Where(pm => pm.IsActive && pm.SuspendedByPlanAt == null && !pm.IsDeleted)
            .Select(pm => new { pm.Id, pm.UserId, pm.ProviderType, pm.ProviderId, pm.Privileges, Name = pm.User.Name })
            .ToListAsync(ct);
        workers = workers.Where(w => WorkerPrivileges.Has(w.Privileges, WorkerPrivileges.Attendance)).ToList();
        if (workers.Count == 0) return 0;

        var actions = 0;
        var missingByProvider = new Dictionary<(string Type, int Id), List<(string? Name, DateTime Start)>>();
        var holidayToday = await AttendanceAccess.IsHolidayAsync(db, todayLocal, ct);

        foreach (var provider in workers.GroupBy(w => (w.ProviderType, w.ProviderId)))
        {
            StationPlanContext plan;
            try { plan = await WorkerAccess.ContextAsync(db, provider.Key.ProviderType, provider.Key.ProviderId, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Attendance tick: plan of {Type} {Id}", provider.Key.ProviderType, provider.Key.ProviderId); continue; }
            if (!plan.HasWorkers) continue;

            var ids = provider.Select(w => w.Id).ToList();
            var schedules = await db.WorkerShiftSchedules.AsNoTracking().Include(x => x.Days).Where(x => ids.Contains(x.ProviderManagerId) && !x.IsDeleted).ToListAsync(ct);
            var days = await db.AttendanceDays.Where(d => ids.Contains(d.ProviderManagerId) && !d.IsDeleted && d.WorkDate >= todayLocal.AddDays(-1)).ToListAsync(ct);
            var events = await db.AttendanceEvents.AsNoTracking()
                .Where(e => e.ProviderType == provider.Key.ProviderType && e.ProviderId == provider.Key.ProviderId && e.WorkDate >= todayLocal.AddDays(-1))
                .Select(e => new { e.ProviderManagerId, e.WorkDate, e.Kind }).ToListAsync(ct);
            bool Sent(int? pmId, DateOnly date, string kind) => events.Any(e => e.ProviderManagerId == pmId && e.WorkDate == date && e.Kind == kind);

            foreach (var w in provider)
            {
                var mine = schedules.Where(x => x.ProviderManagerId == w.Id).ToList();
                var (workDate, shift) = ShiftResolver.ResolveForIn(mine, now, s);
                var day = days.FirstOrDefault(d => d.ProviderManagerId == w.Id && d.WorkDate == workDate);
                var isHoliday = workDate == todayLocal ? holidayToday : await AttendanceAccess.IsHolidayAsync(db, workDate, ct);

                if (shift is not null && day?.FirstInAt is null && !isHoliday)
                {
                    if (now >= shift.StartUtc.AddMinutes(ReminderInMinutes) && now < shift.EndUtc && !Sent(w.Id, workDate, ReminderIn))
                    {
                        await notifier.NotifyWorkerReminderAsync(w.Id, true, shift.StartUtc, ct);
                        Record(provider.Key, w.Id, workDate, ReminderIn, now); actions++;
                    }
                    if (now >= shift.StartUtc.AddMinutes(OwnerAlertMinutes) && now < shift.EndUtc && !Sent(w.Id, workDate, OwnerMissing))
                    {
                        if (!missingByProvider.TryGetValue(provider.Key, out var list)) missingByProvider[provider.Key] = list = [];
                        list.Add((w.Name, shift.StartUtc));
                        Record(provider.Key, w.Id, workDate, OwnerMissing, now); actions++;
                    }
                    if (now >= shift.EndUtc && day is null)
                    {
                        db.AttendanceDays.Add(new AttendanceDay { ProviderManagerId = w.Id, WorkDate = workDate, ShiftStartUtc = shift.StartUtc, ShiftEndUtc = shift.EndUtc, Status = AttendanceStatus.Absent });
                        actions++;
                    }
                }
                else if (shift is not null && day is null && isHoliday && now >= shift.EndUtc)
                {
                    db.AttendanceDays.Add(new AttendanceDay { ProviderManagerId = w.Id, WorkDate = workDate, ShiftStartUtc = shift.StartUtc, ShiftEndUtc = shift.EndUtc, Status = AttendanceStatus.Holiday });
                    actions++;
                }

                var openDay = days.Where(d => d.ProviderManagerId == w.Id && d.IsOpen).OrderByDescending(d => d.WorkDate).FirstOrDefault();
                if (openDay?.ShiftEndUtc is DateTime end && now >= end.AddMinutes(ReminderOutMinutes) && !Sent(w.Id, openDay.WorkDate, ReminderOut))
                {
                    await notifier.NotifyWorkerReminderAsync(w.Id, false, end, ct);
                    Record(provider.Key, w.Id, openDay.WorkDate, ReminderOut, now); actions++;
                }
            }

            // 21:00 Jordan: the owner's summary of today.
            if (JordanTime.FromUtc(now).Hour >= SummaryHour && !Sent(null, todayLocal, DailySummary))
            {
                var today = days.Where(d => d.WorkDate == todayLocal).ToList();
                var present = today.Count(d => d.FirstInAt != null);
                var late = today.Count(d => d.LateMinutes > 0);
                var absent = provider.Count(w =>
                {
                    var shift = ShiftResolver.ForDate(schedules.Where(x => x.ProviderManagerId == w.Id), todayLocal);
                    return shift is not null && !holidayToday && now >= shift.StartUtc.AddMinutes(OwnerAlertMinutes) && today.All(d => d.ProviderManagerId != w.Id || d.FirstInAt == null);
                });
                await notifier.NotifyDailySummaryAsync(provider.Key.ProviderType, provider.Key.ProviderId, present, late, absent, ct);
                Record(provider.Key, null, todayLocal, DailySummary, now); actions++;
            }

            if (missingByProvider.TryGetValue(provider.Key, out var missing) && missing.Count > 0)
            {
                await notifier.NotifyMissingAsync(provider.Key.ProviderType, provider.Key.ProviderId, missing, ct);
                missingByProvider.Remove(provider.Key);
            }
            await db.SaveChanges(ct);
        }
        return actions;
    }

    private void Record((string Type, int Id) provider, int? pmId, DateOnly date, string kind, DateTime now) =>
        db.AttendanceEvents.Add(new AttendanceEvent { ProviderType = provider.Type, ProviderId = provider.Id, ProviderManagerId = pmId, WorkDate = date, Kind = kind, SentAt = now });

    /// <summary>Decision 19: raw coordinates go after 90 days; zone, distance and verdict stay.</summary>
    private async Task<int> ScrubAsync(DateTime now, CancellationToken ct)
    {
        var limit = now.AddDays(-ScrubDays);
        return await db.Punches.Where(p => p.At < limit && (p.Lat != null || p.Lng != null))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Lat, (double?)null).SetProperty(p => p.Lng, (double?)null), ct);
    }
}
