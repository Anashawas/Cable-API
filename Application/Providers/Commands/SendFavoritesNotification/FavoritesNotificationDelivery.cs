using Application.Common.Interfaces;
using Application.NotificationInbox.Helpers;
using Cable.Core;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Providers.Commands.SendFavoritesNotification;

public record ProviderInfo(int? OwnerId, string Name, bool AutoApproveWorkerNotifications);

public record DeliveryOutcome(int RecipientCount, int PushDeliveredCount, Guid BatchId);

/// <summary>
/// Shared machinery for provider fan announcements: provider resolution and the
/// actual delivery (push with routing + inbox rows). Used by the direct send
/// AND by the owner-approval path (F3) so both deliver identically.
/// </summary>
public static class FavoritesNotificationDelivery
{
    public const string StatusPending = "pending";
    public const string StatusSent = "sent";
    public const string StatusRejected = "rejected";

    public const int MaxSendsPerDay = 2;
    public const string AnnouncementTypeName = "provider_announcement";
    public const int FallbackTypeId = 1; // system_announcement

    public static async Task<ProviderInfo> ResolveProviderAsync(
        IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        ProviderInfo? info = providerType switch
        {
            "ChargingPoint" => await db.ChargingPoints.AsNoTracking()
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => new ProviderInfo(x.OwnerId, x.Name, x.AutoApproveWorkerNotifications))
                .FirstOrDefaultAsync(ct),
            "ServiceProvider" => await db.ServiceProviders.AsNoTracking()
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => new ProviderInfo(x.OwnerId, x.Name, x.AutoApproveWorkerNotifications))
                .FirstOrDefaultAsync(ct),
            _ => throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'")
        };

        return info ?? throw new NotFoundException($"can not find {providerType} with id {providerId}");
    }

    /// <summary>Only rows that actually went out count toward the rolling 24h limit (F3).</summary>
    public static async Task<int> CountSentLast24HoursAsync(
        IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        return await db.ProviderFavoriteNotifications.AsNoTracking()
            .CountAsync(x => x.ProviderType == providerType
                             && x.ProviderId == providerId
                             && !x.IsDeleted
                             && x.Status == StatusSent
                             && (x.SentAt ?? x.CreatedAt) >= since, ct);
    }

    public static async Task<List<int>> GetRecipientIdsAsync(
        IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        return providerType == "ChargingPoint"
            ? await db.UserFavoriteChargingPoints.AsNoTracking()
                .Where(f => !f.IsDeleted && f.ChargingPointId == providerId && !f.User.IsDeleted && f.User.IsActive)
                .Select(f => f.UserId)
                .Distinct()
                .ToListAsync(ct)
            : await db.UserFavoriteServiceProviders.AsNoTracking()
                .Where(f => !f.IsDeleted && f.ServiceProviderId == providerId && !f.User.IsDeleted && f.User.IsActive)
                .Select(f => f.UserId)
                .Distinct()
                .ToListAsync(ct);
    }

    /// <summary>
    /// The actual send: FCM push carrying the full routing data (R2/R3) +
    /// inbox rows with the canonical deepLink, all under one BatchId (F1).
    /// Caller persists the audit row.
    /// </summary>
    public static async Task<DeliveryOutcome> DeliverAsync(
        IApplicationDbContext db,
        INotificationService notificationService,
        ILogger logger,
        string providerType,
        int providerId,
        string providerName,
        string title,
        string body,
        int notificationTypeId,
        List<int> recipientIds,
        CancellationToken ct)
    {
        var batchId = Guid.NewGuid();

        // R3: the partner sends only content — WE attach the routing.
        var targetType = NotificationRouting.ProviderTypeToTargetType(providerType);
        var deepLink = NotificationRouting.BuildDeepLink(targetType, providerId);
        var fcmData = NotificationRouting.BuildFcmData(targetType, providerId);

        var pushDelivered = 0;
        var tokens = await db.NotificationTokens.AsNoTracking()
            .Where(t => recipientIds.Contains(t.UserId))
            .Select(t => new { t.AppType, t.Token })
            .ToListAsync(ct);

        foreach (var group in tokens.GroupBy(t => t.AppType))
        {
            try
            {
                var result = await notificationService.SendMessagesAsync(
                    group.Select(x => x.Token).ToList(), title, body, group.Key, fcmData);
                pushDelivered += result.SuccessCount;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Favorites announcement push failed for {ProviderType} {ProviderId} ({AppType})",
                    providerType, providerId, group.Key);
            }
        }

        var data = System.Text.Json.JsonSerializer.Serialize(new
        {
            providerType,
            providerId,
            providerName,
            chargerId = providerType == "ChargingPoint" ? (int?)providerId : null
        });

        await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(
            db, recipientIds, notificationTypeId, title, body, deepLink, data, ct, batchId);

        return new DeliveryOutcome(recipientIds.Count, pushDelivered, batchId);
    }

    /// <summary>
    /// F5 + DECISION-2: server-built title in Arabic — "{nameAr} من {station}"
    /// (e.g. "إعلان من محطة الخليفة"). One push title serves a mixed audience,
    /// and the audience reads Arabic. Falls back to the code name until a type
    /// has its display name seeded.
    /// </summary>
    public static string BuildTitle(string typeName, string providerName) =>
        $"{typeName} من {providerName.Trim()}";
}
