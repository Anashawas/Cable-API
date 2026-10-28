using System.Text.Json;
using Application.CableGifts;
using Application.Common.Interfaces;
using Application.NotificationInbox.Helpers;
using Application.Promos;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructrue.BackgroundJobs;

/// <summary>Hangfire jobs of Cable gifts and promo codes — see <see cref="IGiftsAndPromosJobs"/>.</summary>
public class GiftsAndPromosJobs(
    IApplicationDbContext db,
    INotificationService notificationService,
    ILogger<GiftsAndPromosJobs> logger) : IGiftsAndPromosJobs
{
    public async Task NotifyGiftOrderAsync(int giftOrderId, string kind, CancellationToken cancellationToken = default)
    {
        var order = await db.GiftOrders.AsNoTracking()
            .Where(o => o.Id == giftOrderId)
            .Select(o => new { o.Id, o.UserId, o.OrderCode, o.PointsSpent, o.Status, o.Gift.NameAr, o.Gift.NameEn })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return;

        var data = new Dictionary<string, string>
        {
            ["type"] = GiftPushTexts.DataType,
            ["orderId"] = order.Id.ToString(),
            ["status"] = order.Status.ToString()
        };

        await SendAsync([order.UserId], language => GiftPushTexts.Compose(kind, language, order.OrderCode, order.NameAr,
            order.NameEn, order.PointsSpent), data, GiftPushTexts.NotificationTypeName, cancellationToken);

        logger.LogInformation("NotifyGiftOrder: order {OrderId} {Kind} → user {UserId}", giftOrderId, kind, order.UserId);
    }

    public async Task NotifyPromoAssignedAsync(Guid assignmentBatchId, CancellationToken cancellationToken = default)
    {
        var rows = await db.UserPromos.AsNoTracking()
            .Where(u => u.AssignmentBatchId == assignmentBatchId && u.Status == UserPromoStatus.Available && !u.IsDeleted)
            .Select(u => new { u.UserId, u.PromoCampaignId, u.Campaign.TitleAr, u.Campaign.TitleEn })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return;

        var first = rows[0];
        var data = new Dictionary<string, string>
        {
            ["type"] = PromoPushTexts.AssignedType,
            ["campaignId"] = first.PromoCampaignId.ToString()
        };

        await SendAsync(rows.Select(r => r.UserId).Distinct().ToList(),
            language => PromoPushTexts.Assigned(language, first.TitleAr, first.TitleEn), data,
            PromoPushTexts.AssignedType, cancellationToken);

        logger.LogInformation("NotifyPromoAssigned: batch {BatchId}, campaign {CampaignId}, {Users} user(s)",
            assignmentBatchId, first.PromoCampaignId, rows.Count);
    }

    public async Task<int> RunPromoExpiryAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expired = await db.UserPromos
            .Where(u => u.Status == UserPromoStatus.Available && u.ExpiresAt != null && u.ExpiresAt <= now && !u.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, UserPromoStatus.Expired)
                .SetProperty(u => u.ModifiedAt, now), cancellationToken);

        var soon = now.Add(PromoRules.ExpiringSoonWindow);
        var expiring = await db.UserPromos.AsNoTracking()
            .Where(u => u.Status == UserPromoStatus.Available && !u.IsDeleted && u.ExpiryReminderSentAt == null
                        && u.ExpiresAt != null && u.ExpiresAt > now && u.ExpiresAt <= soon
                        && u.Campaign.IsActive && !u.Campaign.IsDeleted)
            .Select(u => new { u.Id, u.UserId, u.PromoCampaignId, u.Campaign.TitleAr, u.Campaign.TitleEn })
            .ToListAsync(cancellationToken);

        var pushes = 0;
        foreach (var campaign in expiring.GroupBy(e => new { e.PromoCampaignId, e.TitleAr, e.TitleEn }))
        {
            var data = new Dictionary<string, string>
            {
                ["type"] = PromoPushTexts.ExpiringType,
                ["campaignId"] = campaign.Key.PromoCampaignId.ToString()
            };
            await SendAsync(campaign.Select(e => e.UserId).Distinct().ToList(),
                language => PromoPushTexts.Expiring(language, campaign.Key.TitleAr, campaign.Key.TitleEn), data,
                PromoPushTexts.ExpiringType, cancellationToken);

            var ids = campaign.Select(e => e.Id).ToList();
            await db.UserPromos.Where(u => ids.Contains(u.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiryReminderSentAt, now), cancellationToken);
            pushes += ids.Count;
        }

        logger.LogInformation("RunPromoExpiry: {Expired} promo(s) expired, {Reminded} reminder(s) sent", expired, pushes);
        return pushes;
    }

    public async Task DeactivateCampaignIfBudgetShortAsync(int promoCampaignId, decimal neededCableShareJod,
        CancellationToken cancellationToken = default)
    {
        var changed = await db.PromoCampaigns
            .Where(c => c.Id == promoCampaignId && c.IsActive && !c.IsDeleted
                        && c.CableBudgetJod != null && c.CableSpentJod + neededCableShareJod > c.CableBudgetJod)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsActive, false)
                .SetProperty(c => c.DeactivatedReason, PromoDeactivationReasons.BudgetExhausted)
                .SetProperty(c => c.ModifiedAt, DateTime.UtcNow), cancellationToken);

        if (changed > 0)
            logger.LogInformation("Promo campaign {CampaignId} switched off: budget too small for a {Needed} JOD use",
                promoCampaignId, neededCableShareJod);
    }

    /// <summary>One multicast per language (device language, Arabic when unknown) + an inbox row per user.</summary>
    private async Task SendAsync(IReadOnlyCollection<int> userIds, Func<string?, (string Title, string Body)> compose,
        Dictionary<string, string> data, string notificationTypeName, CancellationToken ct)
    {
        var tokens = new List<(int UserId, string Token, string? Language)>();
        foreach (var chunk in userIds.Chunk(2000))
        {
            var ids = chunk.ToList();
            tokens.AddRange((await db.NotificationTokens.AsNoTracking()
                    .Where(n => ids.Contains(n.UserId) && n.AppType == FirebaseAppType.UserApp)
                    .Select(n => new { n.UserId, n.Token, n.Language })
                    .ToListAsync(ct))
                .Select(n => (n.UserId, n.Token, n.Language)));
        }

        static string LangKey(string? language) => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";

        foreach (var group in tokens.GroupBy(t => LangKey(t.Language)))
        {
            var (title, body) = compose(group.Key);
            try
            {
                await notificationService.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body,
                    FirebaseAppType.UserApp, data);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GiftsAndPromos push '{Type}' failed for language {Lang}", notificationTypeName, group.Key);
            }
        }

        // Inbox in each user's language (their first device's; Arabic without a device).
        var typeId = await db.NotificationTypes.AsNoTracking()
            .Where(n => n.Name == notificationTypeName)
            .Select(n => (int?)n.Id)
            .FirstOrDefaultAsync(ct) ?? 1;
        var languageOf = tokens.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => LangKey(g.First().Language));
        var json = JsonSerializer.Serialize(data);
        var batchId = Guid.NewGuid();   // one send = one batch, for the inbox statistics
        foreach (var byLanguage in userIds.GroupBy(id => languageOf.GetValueOrDefault(id, "ar")))
        {
            var (title, body) = compose(byLanguage.Key);
            foreach (var chunk in byLanguage.Chunk(1000))
                await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, chunk, typeId, title, body, null, json, ct, batchId);
        }
    }
}
