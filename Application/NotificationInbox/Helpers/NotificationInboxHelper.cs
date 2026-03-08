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
        CancellationToken cancellationToken)
    {
        var userIdsList = userIds.ToList();

        if (!userIdsList.Any())
        {
            return 0;
        }

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
                Data = data
            });
        }

        context.NotificationInboxes.AddRange(notifications);
        await context.SaveChanges(cancellationToken);

        return notifications.Count;
    }
}
