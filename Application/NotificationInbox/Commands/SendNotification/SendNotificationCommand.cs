using Application.Common.Interfaces;
using Application.NotificationInbox.Helpers;
using Cable.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.AddNotification;

public record SendNotificationCommand(
    List<int> UserIds,
    int NotificationTypeId,
    string Title,
    string Body,
    bool IsForAll = false,
    string? DeepLink = null,
    string? Data = null,
    FirebaseAppType AppType = FirebaseAppType.UserApp
) : IRequest<int>;

public class SendNotificationCommandHandler(
    IApplicationDbContext applicationDbContext,
    INotificationService notificationService)
    : IRequestHandler<SendNotificationCommand, int>
{
    public async Task<int> Handle(SendNotificationCommand request, CancellationToken cancellationToken)
    {
        var notificationCount = 0;

        if (request.IsForAll)
        {
            // Get all user IDs that have tokens registered for the specified app type
            var allUserIds = await applicationDbContext.NotificationTokens
                .AsNoTracking()
                .Where(x => x.AppType == request.AppType)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (allUserIds.Any())
            {
                notificationCount = await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(
                    applicationDbContext,
                    allUserIds,
                    request.NotificationTypeId,
                    request.Title,
                    request.Body,
                    request.DeepLink,
                    request.Data,
                    cancellationToken);
            }

            return notificationCount;
        }

        // Get user tokens for the specified app type only
        var userTokens = await applicationDbContext.NotificationTokens
            .AsNoTracking()
            .Where(x => request.UserIds.Contains(x.UserId) && x.AppType == request.AppType)
            .Select(x => new { x.UserId, x.Token })
            .ToListAsync(cancellationToken);

        var allTokens = userTokens.Select(x => x.Token).ToList();

        if (allTokens.Any())
        {
            // Send notification to the specified Firebase app
            var sendResult = await notificationService.SendMessagesAsync(
                allTokens,
                request.Title,
                request.Body,
                request.AppType);

            var successfulUserIds = userTokens
                .Where(ut => sendResult.SuccessfulTokens.Contains(ut.Token))
                .Select(ut => ut.UserId)
                .Distinct()
                .ToList();

            if (successfulUserIds.Any())
            {
                notificationCount = await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(
                    applicationDbContext,
                    successfulUserIds,
                    request.NotificationTypeId,
                    request.Title,
                    request.Body,
                    request.DeepLink,
                    request.Data,
                    cancellationToken);
            }
        }

        return notificationCount;
    }
}