using System.Text.Json;
using Application.Common.Interfaces;
using Application.NotificationInbox.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.ChargingPoints;

/// <summary>
/// Push + inbox notifications for the station update-request workflow
/// (admins on submit, the owner on approve/reject). Best-effort: failures are
/// logged and swallowed — a Firebase hiccup must never fail the main operation.
/// </summary>
public static class UpdateRequestNotifier
{
    private const int AdminRoleId = 2; // keep in sync with AdminRoleGuard.AdminRoleId
    private const string SubmittedTypeName = "update_request_submitted";
    private const string DecidedTypeName = "update_request_decided";
    private const int FallbackTypeId = 1; // system_announcement

    /// <summary>Notifies every admin that an owner submitted a new update request.</summary>
    public static async Task NotifyAdminsNewRequestAsync(
        IApplicationDbContext db,
        INotificationService notifications,
        ILogger logger,
        int updateRequestId,
        int chargingPointId,
        int requestedByUserId,
        CancellationToken ct)
    {
        try
        {
            var stationName = await StationNameAsync(db, chargingPointId, ct);
            var requesterName = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Id == requestedByUserId)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(ct);

            var adminIds = await db.UserAccounts.AsNoTracking()
                .Where(u => !u.IsDeleted && u.RoleId == AdminRoleId)
                .Select(u => u.Id)
                .ToListAsync(ct);
            if (adminIds.Count == 0) return;

            var title = "طلب تعديل محطة جديد";
            var body = $"قدّم {requesterName ?? "مالك المحطة"} طلب تعديل لمحطة {stationName}";
            var data = JsonSerializer.Serialize(new { updateRequestId, chargingPointId });

            await SendAsync(db, notifications, logger, adminIds, SubmittedTypeName, title, body, data, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify admins about update request {UpdateRequestId}", updateRequestId);
        }
    }

    /// <summary>Notifies the requesting owner that their request was approved or rejected (with the reason).</summary>
    public static async Task NotifyOwnerDecisionAsync(
        IApplicationDbContext db,
        INotificationService notifications,
        ILogger logger,
        int updateRequestId,
        int chargingPointId,
        int ownerUserId,
        bool approved,
        string? rejectionReason,
        CancellationToken ct)
    {
        try
        {
            var stationName = await StationNameAsync(db, chargingPointId, ct);

            var title = approved ? "تمت الموافقة على طلب التعديل" : "تم رفض طلب التعديل";
            var body = approved
                ? $"تمت الموافقة على طلب تعديل محطة {stationName} وتم تطبيق التغييرات"
                : $"تم رفض طلب تعديل محطة {stationName}" +
                  (string.IsNullOrWhiteSpace(rejectionReason) ? "" : $"، السبب: {rejectionReason}");
            var data = JsonSerializer.Serialize(new { updateRequestId, chargingPointId, approved });

            await SendAsync(db, notifications, logger, [ownerUserId], DecidedTypeName, title, body, data, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify owner about update request {UpdateRequestId} decision", updateRequestId);
        }
    }

    private static Task<string?> StationNameAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
        => db.ChargingPoints.AsNoTracking()
            .Where(c => c.Id == chargingPointId)
            .Select(c => (string?)c.Name)
            .FirstOrDefaultAsync(ct);

    private static async Task SendAsync(
        IApplicationDbContext db,
        INotificationService notifications,
        ILogger logger,
        List<int> userIds,
        string typeName,
        string title,
        string body,
        string data,
        CancellationToken ct)
    {
        // Push to every device the targets have, grouped per Firebase app
        // (in practice provider tokens are registered under the user app).
        var tokens = await db.NotificationTokens.AsNoTracking()
            .Where(t => userIds.Contains(t.UserId))
            .Select(t => new { t.AppType, t.Token })
            .ToListAsync(ct);

        foreach (var group in tokens.GroupBy(t => t.AppType))
        {
            try
            {
                await notifications.SendMessagesAsync(group.Select(x => x.Token).ToList(), title, body, group.Key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM send failed for app type {AppType}", group.Key);
            }
        }

        // Inbox rows for ALL targets — a transactional event stays visible
        // in-app even for users without a registered device token.
        var typeId = await db.NotificationTypes.AsNoTracking()
            .Where(t => t.Name == typeName)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct) ?? FallbackTypeId;

        await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(
            db, userIds, typeId, title, body, null, data, ct);
    }
}
