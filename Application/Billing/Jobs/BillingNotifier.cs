using System.Text.Json;
using Application.NotificationInbox.Helpers;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Billing.Jobs;

/// <summary>NotificationType names seeded by Scripts/Sprint5_PartnerBilling.sql.</summary>
public static class BillingNotificationTypes
{
    public const string Subscription = "billing_subscription";
    public const string Bundle = "billing_bundle";
    public const string UpgradeRequest = "billing_upgrade_request";
    public const string AdminAlert = "billing_admin_alert";
}

/// <summary>
/// Billing pushes + inbox, each event at most once: the <see cref="BillingEvent"/> row with the event's key
/// is written first (unique index), and an event whose key exists is skipped. Owners get the partner app
/// (StationApp) in each device's language; admins get every app they are signed into.
/// </summary>
public sealed class BillingNotifier(IApplicationDbContext db, INotificationService notifications, ILogger logger)
{
    public delegate (string Title, string Body) Compose(string? language);

    public Task<bool> AlreadySentAsync(string eventKey, CancellationToken ct) =>
        db.BillingEvents.AsNoTracking().AnyAsync(e => e.EventKey == eventKey, ct);

    /// <summary>Records the event without sending (e.g. a lower threshold that was skipped). False if it already existed.</summary>
    public async Task<bool> RecordAsync(BillingEvent e, CancellationToken ct)
    {
        if (await AlreadySentAsync(e.EventKey, ct)) return false;
        e.CreatedAt = DateTime.UtcNow;
        db.BillingEvents.Add(e);
        try
        {
            await db.SaveChanges(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another run wrote the same key first: that run sends it.
            db.BillingEvents.Remove(e);
            return false;
        }
    }

    public async Task<bool> NotifyOwnerAsync(string eventKey, string kind, string providerType, int providerId,
        int? planSubscriptionId, int? subscriptionId, Compose compose, Dictionary<string, string> data, CancellationToken ct)
    {
        var owner = await BillingAccessGuard.OwnerOfAsync(db, providerType, providerId, ct);
        var (arTitle, arBody) = compose("ar");
        var evt = new BillingEvent
        {
            EventKey = eventKey, Kind = kind, Audience = "owner", ProviderType = providerType, ProviderId = providerId,
            PlanSubscriptionId = planSubscriptionId, SubscriptionId = subscriptionId, Title = Trim(arTitle, 300), Body = Trim(arBody, 1000)
        };
        if (!await RecordAsync(evt, ct)) return false;
        if (owner.OwnerId is not int ownerId) return true;

        data["type"] = kind;
        data["providerType"] = providerType;
        data["providerId"] = providerId.ToString();

        var tokens = await db.NotificationTokens.AsNoTracking()
            .Where(t => t.UserId == ownerId && t.AppType == FirebaseAppType.StationApp)
            .Select(t => new { t.Token, t.Language })
            .ToListAsync(ct);
        foreach (var group in tokens.GroupBy(t => string.Equals(t.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar"))
        {
            var (title, body) = compose(group.Key);
            try { await notifications.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body, FirebaseAppType.StationApp, data); }
            catch (Exception ex) { logger.LogWarning(ex, "Billing push {EventKey} failed ({Lang})", eventKey, group.Key); }
        }

        var (inboxTitle, inboxBody) = compose(tokens.FirstOrDefault()?.Language);
        await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, [ownerId], await TypeIdAsync(OwnerTypeFor(kind), ct),
            inboxTitle, inboxBody, null, JsonSerializer.Serialize(data), ct);
        return true;
    }

    public async Task<bool> NotifyAdminsAsync(string eventKey, string kind, string? providerType, int? providerId,
        int? planSubscriptionId, Compose compose, Dictionary<string, string> data, CancellationToken ct)
    {
        var (arTitle, arBody) = compose("ar");
        var evt = new BillingEvent
        {
            EventKey = eventKey, Kind = kind, Audience = "admin", ProviderType = providerType, ProviderId = providerId,
            PlanSubscriptionId = planSubscriptionId, Title = Trim(arTitle, 300), Body = Trim(arBody, 1000)
        };
        if (!await RecordAsync(evt, ct)) return false;

        data["type"] = kind;
        if (providerType is not null) data["providerType"] = providerType;
        if (providerId is not null) data["providerId"] = providerId.Value.ToString();

        var admins = await db.UserAccounts.AsNoTracking()
            .Where(u => u.RoleId == Common.Security.AdminRoleGuard.AdminRoleId && u.IsActive && !u.IsDeleted)
            .Select(u => u.Id).ToListAsync(ct);
        if (admins.Count == 0) return true;

        var tokens = await db.NotificationTokens.AsNoTracking()
            .Where(t => admins.Contains(t.UserId))
            .Select(t => new { t.Token, t.AppType, t.Language })
            .ToListAsync(ct);
        foreach (var group in tokens.GroupBy(t => (t.AppType, Lang: string.Equals(t.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar")))
        {
            var (title, body) = compose(group.Key.Lang);
            try { await notifications.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body, group.Key.AppType, data); }
            catch (Exception ex) { logger.LogWarning(ex, "Billing admin push {EventKey} failed", eventKey); }
        }
        await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, admins,
            await TypeIdAsync(kind.StartsWith("upgradeRequest", StringComparison.Ordinal) ? BillingNotificationTypes.UpgradeRequest : BillingNotificationTypes.AdminAlert, ct),
            arTitle, arBody, null, JsonSerializer.Serialize(data), ct);
        return true;
    }

    private static string OwnerTypeFor(string kind) =>
        kind.StartsWith("bundle", StringComparison.Ordinal) ? BillingNotificationTypes.Bundle : BillingNotificationTypes.Subscription;

    private async Task<int> TypeIdAsync(string typeName, CancellationToken ct) =>
        await db.NotificationTypes.AsNoTracking().Where(t => t.Name == typeName).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct) ?? 1;

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
