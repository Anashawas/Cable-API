using System.Globalization;
using System.Text.Json;
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
/// Attendance pushes to the station team (§3.10): a refused attempt or a punch that needs a look goes to the owner
/// and to workers with ViewAttendance. Partner app only, device language, plus an inbox row. Never throws.
/// </summary>
public class AttendanceNotifier(IApplicationDbContext db, INotificationService notifications, ILogger<AttendanceNotifier> logger)
{
    public const string AttemptType = "attendance_attempt";

    public async Task NotifyAttemptAsync(int punchId, CancellationToken ct = default)
    {
        try
        {
            var p = await db.Punches.AsNoTracking()
                .Where(x => x.Id == punchId)
                .Select(x => new { x.Id, x.Verdict, x.Reason, x.DistanceMeters, x.AccuracyMeters, x.ProviderManagerId,
                    x.ProviderManager.ProviderType, x.ProviderManager.ProviderId, WorkerName = x.ProviderManager.User.Name,
                    ZoneName = db.PunchZones.Where(z => z.Id == x.PunchZoneId).Select(z => z.Name).FirstOrDefault() })
                .FirstOrDefaultAsync(ct);
            if (p is null) return;

            var recipients = await TeamAsync(p.ProviderType, p.ProviderId, ct);
            if (recipients.Count == 0) return;

            var name = string.IsNullOrWhiteSpace(p.WorkerName) ? Lang.Pick("موظف", "A worker") : p.WorkerName;
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

            var data = new Dictionary<string, string>
            {
                ["type"] = AttemptType,
                ["punchId"] = p.Id.ToString(CultureInfo.InvariantCulture),
                ["providerManagerId"] = p.ProviderManagerId.ToString(CultureInfo.InvariantCulture),
                ["providerType"] = p.ProviderType,
                ["providerId"] = p.ProviderId.ToString(CultureInfo.InvariantCulture),
                ["verdict"] = p.Verdict,
                ["reason"] = p.Reason ?? "",
            };

            var tokens = await db.NotificationTokens.AsNoTracking()
                .Where(t => recipients.Contains(t.UserId) && t.AppType == FirebaseAppType.StationApp)
                .Select(t => new { t.UserId, t.Token, t.Language }).ToListAsync(ct);
            foreach (var group in tokens.GroupBy(t => string.Equals(t.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar"))
            {
                var (title, body) = Text(group.Key);
                try { await notifications.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body, FirebaseAppType.StationApp, data); }
                catch (Exception ex) { logger.LogWarning(ex, "Attendance push failed ({Lang})", group.Key); }
            }

            var typeId = await db.NotificationTypes.AsNoTracking().Where(t => t.Name == AttemptType).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct) ?? 1;
            var languageOf = tokens.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.First().Language);
            var json = JsonSerializer.Serialize(data);
            foreach (var byLang in recipients.GroupBy(u => string.Equals(languageOf.GetValueOrDefault(u), "en", StringComparison.OrdinalIgnoreCase)))
            {
                var (title, body) = Text(byLang.Key ? "en" : "ar");
                await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, byLang, typeId, title, body, null, json, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Attendance attempt notification failed for punch {PunchId}", punchId);
        }
    }

    /// <summary>The owner plus usable workers granted ViewAttendance (the plan allowed punching, so it allows viewing).</summary>
    private async Task<List<int>> TeamAsync(string providerType, int providerId, CancellationToken ct)
    {
        var ids = new List<int>();
        var ownerId = providerType == WorkerOwnershipHelper.ChargingPoint
            ? await db.ChargingPoints.AsNoTracking().Where(c => c.Id == providerId).Select(c => c.OwnerId).FirstOrDefaultAsync(ct)
            : await db.ServiceProviders.AsNoTracking().Where(s => s.Id == providerId).Select(s => s.OwnerId).FirstOrDefaultAsync(ct);
        if (ownerId is int o) ids.Add(o);
        var viewers = await db.ProviderManagers.AsNoTracking()
            .Where(pm => pm.ProviderType == providerType && pm.ProviderId == providerId && pm.IsActive && pm.SuspendedByPlanAt == null && !pm.IsDeleted)
            .Select(pm => new { pm.UserId, pm.Privileges }).ToListAsync(ct);
        ids.AddRange(viewers.Where(v => WorkerPrivileges.Has(v.Privileges, WorkerPrivileges.ViewAttendance)).Select(v => v.UserId));
        return ids.Distinct().ToList();
    }
}
