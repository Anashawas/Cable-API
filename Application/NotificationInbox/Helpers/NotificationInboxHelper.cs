using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Helpers;

public static class NotificationInboxHelper
{
    public static async Task<int> CreateNotificationInboxRecordsAsync(
        IApplicationDbContext context,
        IEnumerable<int> userIds,
        int notificationTypeId,
        string title,
        string body,
        string? deepLink,
        string? data,
        CancellationToken cancellationToken,
        Guid? batchId = null)
    {
        var userIdsList = userIds.ToList();

        if (!userIdsList.Any())
        {
            return 0;
        }

        // If no batchId provided, generate one so every send is still trackable as a group.
        var effectiveBatchId = batchId ?? Guid.NewGuid();

        var notifications = new List<Domain.Enitites.NotificationInbox>();

        foreach (var userId in userIdsList)
        {
            notifications.Add(new Domain.Enitites.NotificationInbox
            {
                UserId = userId,
                NotificationTypeId = notificationTypeId,
                Title = title,
                Body = body,
                IsRead = false,
                DeepLink = deepLink,
                Data = data,
                BatchId = effectiveBatchId
            });
        }

        context.NotificationInboxes.AddRange(notifications);
        await context.SaveChanges(cancellationToken);

        return notifications.Count;
    }
}
