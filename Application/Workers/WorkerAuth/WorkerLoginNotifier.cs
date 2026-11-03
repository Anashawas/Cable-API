using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using Application.NotificationInbox.Helpers;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Application.Workers.WorkerAuth;

/// <summary>
/// Pushes of the worker login flow (§3.10): the owner hears when a worker signs in for the first time.
/// Partner app only (StationApp tokens), in the device language, plus an inbox row. Never throws.
/// </summary>
public class WorkerLoginNotifier(IApplicationDbContext db, INotificationService notifications, ILogger<WorkerLoginNotifier> logger)
{
    public const string FirstLoginType = "worker_first_login";

    public async Task NotifyFirstLoginAsync(int workerUserId, CancellationToken ct = default)
    {
        try
        {
            var pm = await db.ProviderManagers.AsNoTracking()
                .Where(m => m.UserId == workerUserId && !m.IsDeleted)
                .Select(m => new { m.Id, m.ProviderType, m.ProviderId, WorkerName = m.User.Name })
                .FirstOrDefaultAsync(ct);
            if (pm is null) return;

            var provider = pm.ProviderType == WorkerOwnershipHelper.ChargingPoint
                ? await db.ChargingPoints.AsNoTracking().Where(c => c.Id == pm.ProviderId).Select(c => new { c.Name, c.OwnerId }).FirstOrDefaultAsync(ct)
                : await db.ServiceProviders.AsNoTracking().Where(s => s.Id == pm.ProviderId).Select(s => new { s.Name, s.OwnerId }).FirstOrDefaultAsync(ct);
            if (provider?.OwnerId is not int ownerId) return;

            var worker = string.IsNullOrWhiteSpace(pm.WorkerName) ? Lang.Pick("الموظف", "The worker") : pm.WorkerName;
            (string Title, string Body) Text(string? lang) => (
                Lang.Pick(lang, $"{worker} دخل لأول مرة ✔", $"{worker} signed in for the first time ✔"),
                Lang.Pick(lang, $"حسابه صار فعّال بـ«{provider.Name}»", $"Their account is now active at \"{provider.Name}\""));

            var data = new Dictionary<string, string>
            {
                ["type"] = FirstLoginType,
                ["providerManagerId"] = pm.Id.ToString(CultureInfo.InvariantCulture),
                ["providerType"] = pm.ProviderType,
                ["providerId"] = pm.ProviderId.ToString(CultureInfo.InvariantCulture),
            };

            var tokens = await db.NotificationTokens.AsNoTracking()
                .Where(t => t.UserId == ownerId && t.AppType == FirebaseAppType.StationApp)
                .Select(t => new { t.Token, t.Language })
                .ToListAsync(ct);
            foreach (var group in tokens.GroupBy(t => string.Equals(t.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar"))
            {
                var (title, body) = Text(group.Key);
                try { await notifications.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body, FirebaseAppType.StationApp, data); }
                catch (Exception ex) { logger.LogWarning(ex, "Worker first-login push failed ({Lang})", group.Key); }
            }

            var typeId = await db.NotificationTypes.AsNoTracking().Where(t => t.Name == FirstLoginType).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct) ?? 1;
            var (inboxTitle, inboxBody) = Text(tokens.FirstOrDefault()?.Language);
            await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, [ownerId], typeId, inboxTitle, inboxBody, null, JsonSerializer.Serialize(data), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Worker first-login notification failed for user {UserId}", workerUserId);
        }
    }
}
