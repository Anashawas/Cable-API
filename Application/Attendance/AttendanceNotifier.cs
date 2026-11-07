using System.Globalization;
using System.Text.Json;
using Application.Attendance.Punching;
using Application.Common.Interfaces;
using Application.NotificationInbox.Helpers;
using Application.Workers;
using Cable.Core.Constants;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Attendance;

/// <summary>
/// Attendance pushes (§3.10). Partner app only (StationApp tokens), device language, plus an inbox row. Never throws.
/// Types: attendance_attempt (team), attendance_reminder (worker), attendance_missing (team, grouped),
/// attendance_manual (owner), attendance_correction (worker), attendance_summary (owner).
/// </summary>
public class AttendanceNotifier(IApplicationDbContext db, INotificationService notifications, ILogger<AttendanceNotifier> logger)
{
    public const string AttemptType = "attendance_attempt";
    public const string ReminderType = "attendance_reminder";
    public const string MissingType = "attendance_missing";
    public const string ManualType = "attendance_manual";
    public const string CorrectionType = "attendance_correction";
    public const string SummaryType = "attendance_summary";

    public async Task NotifyAttemptAsync(int punchId, CancellationToken ct = default)
    {
        var p = await db.Punches.AsNoTracking()
            .Where(x => x.Id == punchId)
            .Select(x => new { x.Id, x.Verdict, x.Reason, x.DistanceMeters, x.AccuracyMeters, x.ProviderManagerId,
                x.ProviderManager.ProviderType, x.ProviderManager.ProviderId, WorkerName = x.ProviderManager.User.Name,
                ZoneName = db.PunchZones.Where(z => z.Id == x.PunchZoneId).Select(z => z.Name).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        if (p is null) return;

        var name = WorkerName(p.WorkerName);
        (string Title, string Body) Text(string? lang) => p.Reason switch
        {
            PunchReason.OutsideZone => (Lang.Pick(lang, "محاولة بصمة مرفوضة", "Punch refused"),
                Lang.Pick(lang, $"{name} حاول يسجّل من بعد {p.DistanceMeters} م عن «{p.ZoneName}»", $"{name} tried to punch {p.DistanceMeters} m from \"{p.ZoneName}\"")),
            PunchReason.LowAccuracy => (Lang.Pick(lang, "محاولة بصمة مرفوضة", "Punch refused"),
                Lang.Pick(lang, $"{name} حاول يسجّل بإشارة ضعيفة (±{p.AccuracyMeters} م)", $"{name} tried to punch with a weak signal (±{p.AccuracyMeters} m)")),
            PunchReason.Mocked => (Lang.Pick(lang, "بصمة للمراجعة", "Punch to review"),
                Lang.Pick(lang, $"{name} سجّل بموقع وهمي محتمل", $"{name} punched with a possibly fake location")),
            _ => (Lang.Pick(lang, "بصمة للمراجعة", "Punch to review"),
                Lang.Pick(lang, $"{name} سجّل خارج المنطقة بشوي ({p.DistanceMeters} م) — الموقع مش دقيق", $"{name} punched just outside the zone ({p.DistanceMeters} m) — imprecise position")),
        };
        var data = Data(AttemptType, p.ProviderType, p.ProviderId, ("punchId", p.Id), ("providerManagerId", p.ProviderManagerId));
        data["verdict"] = p.Verdict; data["reason"] = p.Reason ?? "";
        await SendAsync(await TeamAsync(p.ProviderType, p.ProviderId, ct), Text, AttemptType, data, ct);
    }

    /// <summary>Worker: "your shift started — punch in" (+5 min) / "don't forget to punch out" (+15 min after the end).</summary>
    public async Task NotifyWorkerReminderAsync(int providerManagerId, bool punchIn, DateTime shiftTimeUtc, CancellationToken ct = default)
    {
        var pm = await db.ProviderManagers.AsNoTracking().Where(x => x.Id == providerManagerId)
            .Select(x => new { x.UserId, x.ProviderType, x.ProviderId }).FirstOrDefaultAsync(ct);
        if (pm is null) return;
        (string Title, string Body) Text(string? lang) => punchIn
            ? (Lang.Pick(lang, "ورديتك بلّشت", "Your shift has started"), Lang.Pick(lang, $"ورديتك بلّشت {Clock(shiftTimeUtc, lang)} — سجّل حضورك", $"Your shift started at {Clock(shiftTimeUtc, lang)} — punch in"))
            : (Lang.Pick(lang, "لا تنسى تسجّل انصرافك", "Don't forget to punch out"), Lang.Pick(lang, $"خلصت ورديتك {Clock(shiftTimeUtc, lang)} وما سجّلت انصراف", $"Your shift ended at {Clock(shiftTimeUtc, lang)} and you have not punched out"));
        var data = Data(ReminderType, pm.ProviderType, pm.ProviderId, ("providerManagerId", providerManagerId));
        data["kind"] = punchIn ? "in" : "out";
        await SendAsync([pm.UserId], Text, ReminderType, data, ct);
    }

    /// <summary>Team: "{name} did not punch in (shift 6:00)", or "3 workers did not punch in" when several at once.</summary>
    public async Task NotifyMissingAsync(string providerType, int providerId, List<(string? Name, DateTime ShiftStartUtc)> workers, CancellationToken ct = default)
    {
        if (workers.Count == 0) return;
        (string Title, string Body) Text(string? lang) => workers.Count == 1
            ? (Lang.Pick(lang, "موظف ما سجّل حضور", "A worker did not punch in"),
               Lang.Pick(lang, $"{WorkerName(workers[0].Name)} ما سجّل حضور (ورديته {Clock(workers[0].ShiftStartUtc, lang)})", $"{WorkerName(workers[0].Name)} did not punch in (shift {Clock(workers[0].ShiftStartUtc, lang)})"))
            : (Lang.Pick(lang, $"{workers.Count} موظفين ما سجّلوا حضور", $"{workers.Count} workers did not punch in"),
               string.Join(" · ", workers.Select(w => $"{WorkerName(w.Name)} ({Clock(w.ShiftStartUtc, lang)})")));
        var data = Data(MissingType, providerType, providerId);
        data["count"] = workers.Count.ToString(CultureInfo.InvariantCulture);
        await SendAsync(await TeamAsync(providerType, providerId, ct), Text, MissingType, data, ct);
    }

    /// <summary>Owner: a supervisor recorded a worker's shift by hand.</summary>
    public async Task NotifyManualAsync(int punchId, CancellationToken ct = default)
    {
        var p = await db.Punches.AsNoTracking().Where(x => x.Id == punchId)
            .Select(x => new { x.Id, x.Direction, x.Note, x.RecordedByUserId, x.ProviderManagerId, x.ProviderManager.ProviderType, x.ProviderManager.ProviderId,
                WorkerName = x.ProviderManager.User.Name, RecorderName = db.UserAccounts.Where(u => u.Id == x.RecordedByUserId).Select(u => u.Name).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        if (p is null) return;
        var ownerId = await OwnerAsync(p.ProviderType, p.ProviderId, ct);
        if (ownerId is null || ownerId == p.RecordedByUserId) return;
        var recorder = WorkerName(p.RecorderName); var worker = WorkerName(p.WorkerName);
        (string Title, string Body) Text(string? lang) => (
            Lang.Pick(lang, "تسجيل دوام يدوي", "Manual attendance entry"),
            p.Direction == PunchDirection.In
                ? Lang.Pick(lang, $"{recorder} (مشرف) بدأ دوام {worker} يدوياً: «{p.Note}»", $"{recorder} (supervisor) started {worker}'s shift by hand: \"{p.Note}\"")
                : Lang.Pick(lang, $"{recorder} (مشرف) أنهى دوام {worker} يدوياً: «{p.Note}»", $"{recorder} (supervisor) ended {worker}'s shift by hand: \"{p.Note}\""));
        await SendAsync([ownerId.Value], Text, ManualType, Data(ManualType, p.ProviderType, p.ProviderId, ("punchId", p.Id), ("providerManagerId", p.ProviderManagerId)), ct);
    }

    /// <summary>Worker: the owner corrected one of their days.</summary>
    public async Task NotifyCorrectionAsync(int attendanceDayId, string summaryAr, string summaryEn, CancellationToken ct = default)
    {
        var d = await db.AttendanceDays.AsNoTracking().Where(x => x.Id == attendanceDayId)
            .Select(x => new { x.Id, x.WorkDate, x.ProviderManagerId, x.ProviderManager.UserId, x.ProviderManager.ProviderType, x.ProviderManager.ProviderId }).FirstOrDefaultAsync(ct);
        if (d is null) return;
        (string Title, string Body) Text(string? lang) => (Lang.Pick(lang, "تعديل على دوامك", "Your attendance was corrected"), Lang.Pick(lang, summaryAr, summaryEn));
        var data = Data(CorrectionType, d.ProviderType, d.ProviderId, ("attendanceDayId", d.Id), ("providerManagerId", d.ProviderManagerId));
        data["workDate"] = d.WorkDate.ToString("yyyy-MM-dd");
        await SendAsync([d.UserId], Text, CorrectionType, data, ct);
    }

    /// <summary>Owner, 21:00: "Today: 4 present · 1 late · 1 absent".</summary>
    public async Task NotifyDailySummaryAsync(string providerType, int providerId, int present, int late, int absent, CancellationToken ct = default)
    {
        var ownerId = await OwnerAsync(providerType, providerId, ct);
        if (ownerId is null) return;
        (string Title, string Body) Text(string? lang) => (Lang.Pick(lang, "ملخص الدوام اليوم", "Today's attendance"),
            Lang.Pick(lang, $"اليوم: {present} حضروا · {late} تأخر · {absent} غاب", $"Today: {present} present · {late} late · {absent} absent"));
        await SendAsync([ownerId.Value], Text, SummaryType, Data(SummaryType, providerType, providerId), ct);
    }

    // ---- plumbing ----------------------------------------------------------------------------------------------

    private static string WorkerName(string? name) => string.IsNullOrWhiteSpace(name) ? Lang.Pick("موظف", "A worker") : name;

    private static string Clock(DateTime utc, string? lang)
    {
        var l = JordanTime.FromUtc(utc);
        var h = l.Hour % 12 == 0 ? 12 : l.Hour % 12;
        return Lang.Pick(lang, $"{h}:{l:mm} {(l.Hour < 12 ? "ص" : "م")}", $"{h}:{l:mm} {(l.Hour < 12 ? "AM" : "PM")}");
    }

    private static Dictionary<string, string> Data(string type, string providerType, int providerId, params (string Key, int Value)[] extra)
    {
        var d = new Dictionary<string, string> { ["type"] = type, ["providerType"] = providerType, ["providerId"] = providerId.ToString(CultureInfo.InvariantCulture) };
        foreach (var (k, v) in extra) d[k] = v.ToString(CultureInfo.InvariantCulture);
        return d;
    }

    private async Task<int?> OwnerAsync(string providerType, int providerId, CancellationToken ct) =>
        providerType == WorkerOwnershipHelper.ChargingPoint
            ? await db.ChargingPoints.AsNoTracking().Where(c => c.Id == providerId).Select(c => c.OwnerId).FirstOrDefaultAsync(ct)
            : await db.ServiceProviders.AsNoTracking().Where(s => s.Id == providerId).Select(s => s.OwnerId).FirstOrDefaultAsync(ct);

    /// <summary>The owner plus usable workers granted ViewAttendance.</summary>
    private async Task<List<int>> TeamAsync(string providerType, int providerId, CancellationToken ct)
    {
        var ids = new List<int>();
        if (await OwnerAsync(providerType, providerId, ct) is int o) ids.Add(o);
        var viewers = await db.ProviderManagers.AsNoTracking()
            .Where(pm => pm.ProviderType == providerType && pm.ProviderId == providerId && pm.IsActive && pm.SuspendedByPlanAt == null && !pm.IsDeleted)
            .Select(pm => new { pm.UserId, pm.Privileges }).ToListAsync(ct);
        ids.AddRange(viewers.Where(v => WorkerPrivileges.Has(v.Privileges, WorkerPrivileges.ViewAttendance)).Select(v => v.UserId));
        return ids.Distinct().ToList();
    }

    private async Task SendAsync(List<int> userIds, Func<string?, (string Title, string Body)> text, string typeName, Dictionary<string, string> data, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        try
        {
            var tokens = await db.NotificationTokens.AsNoTracking()
                .Where(t => userIds.Contains(t.UserId) && t.AppType == FirebaseAppType.StationApp)
                .Select(t => new { t.UserId, t.Token, t.Language }).ToListAsync(ct);
            foreach (var group in tokens.GroupBy(t => string.Equals(t.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar"))
            {
                var (title, body) = text(group.Key);
                try { await notifications.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body, FirebaseAppType.StationApp, data); }
                catch (Exception ex) { logger.LogWarning(ex, "Attendance push {Type} failed ({Lang})", typeName, group.Key); }
            }

            var typeId = await db.NotificationTypes.AsNoTracking().Where(t => t.Name == typeName).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct) ?? 1;
            var languageOf = tokens.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.First().Language);
            var json = JsonSerializer.Serialize(data);
            foreach (var byLang in userIds.GroupBy(u => string.Equals(languageOf.GetValueOrDefault(u), "en", StringComparison.OrdinalIgnoreCase)))
            {
                var (title, body) = text(byLang.Key ? "en" : "ar");
                await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, byLang, typeId, title, body, null, json, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Attendance notification {Type} failed", typeName);
        }
    }
}
