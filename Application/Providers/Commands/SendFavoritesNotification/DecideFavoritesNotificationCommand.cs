using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Providers.Commands.SendFavoritesNotification;

public record DecideFavoritesNotificationResult(
    int NotificationId,
    string Status,
    int RecipientCount,
    int PushDeliveredCount);

/// <summary>
/// F3: owner/admin decision on a worker's pending fan announcement.
/// Approve = deliver NOW (rate limit checked at this moment, because the limit
/// counts actual sends). Reject = discard, nothing is delivered.
/// </summary>
public record DecideFavoritesNotificationCommand(int NotificationId, bool Approve)
    : IRequest<DecideFavoritesNotificationResult>;

public class DecideFavoritesNotificationCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    INotificationService notificationService,
    ILogger<DecideFavoritesNotificationCommandHandler> logger)
    : IRequestHandler<DecideFavoritesNotificationCommand, DecideFavoritesNotificationResult>
{
    public async Task<DecideFavoritesNotificationResult> Handle(DecideFavoritesNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var notification = await applicationDbContext.ProviderFavoriteNotifications
                               .FirstOrDefaultAsync(x => x.Id == request.NotificationId && !x.IsDeleted,
                                   cancellationToken)
                           ?? throw new NotFoundException(
                               $"can not find favorites notification with id {request.NotificationId}");

        if (notification.Status != FavoritesNotificationDelivery.StatusPending)
            throw new DataValidationException("Status",
                $"This notification is already '{notification.Status}' — only pending ones can be decided.");

        var provider = await FavoritesNotificationDelivery.ResolveProviderAsync(
            applicationDbContext, notification.ProviderType, notification.ProviderId, cancellationToken);

        // Deciding is for the OWNER or an admin — the submitting worker cannot self-approve.
        var isAdmin = await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        if (provider.OwnerId != userId && !isAdmin)
            throw new ForbiddenAccessException("Only the provider owner or an admin can approve or reject worker notifications.");

        notification.DecidedByUserId = userId;
        notification.DecidedAt = DateTime.UtcNow;

        if (!request.Approve)
        {
            notification.Status = FavoritesNotificationDelivery.StatusRejected;
            await applicationDbContext.SaveChanges(cancellationToken);
            return new DecideFavoritesNotificationResult(notification.Id,
                notification.Status, notification.RecipientCount, 0);
        }

        // Approval = the actual send → the rate limit applies here (F3).
        var sentToday = await FavoritesNotificationDelivery.CountSentLast24HoursAsync(
            applicationDbContext, notification.ProviderType, notification.ProviderId, cancellationToken);
        if (sentToday >= FavoritesNotificationDelivery.MaxSendsPerDay)
            throw new DataValidationException("Limit",
                $"This provider already sent {FavoritesNotificationDelivery.MaxSendsPerDay} notifications in the last 24 hours. Approve it again later.");

        var recipientIds = await FavoritesNotificationDelivery.GetRecipientIdsAsync(
            applicationDbContext, notification.ProviderType, notification.ProviderId, cancellationToken);
        if (recipientIds.Count == 0)
            throw new DataValidationException("Favorites", "No users favorite this provider anymore.");

        var typeId = notification.NotificationTypeId
                     ?? await applicationDbContext.NotificationTypes.AsNoTracking()
                         .Where(t => t.Name == FavoritesNotificationDelivery.AnnouncementTypeName)
                         .Select(t => (int?)t.Id)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? FavoritesNotificationDelivery.FallbackTypeId;

        var outcome = await FavoritesNotificationDelivery.DeliverAsync(
            applicationDbContext, notificationService, logger,
            notification.ProviderType, notification.ProviderId, provider.Name,
            notification.Title, notification.Body, typeId, recipientIds, cancellationToken);

        notification.Status = FavoritesNotificationDelivery.StatusSent;
        notification.RecipientCount = outcome.RecipientCount;
        notification.DeliveredCount = outcome.PushDeliveredCount;
        notification.BatchId = outcome.BatchId;
        notification.SentAt = DateTime.UtcNow;
        await applicationDbContext.SaveChanges(cancellationToken);

        return new DecideFavoritesNotificationResult(notification.Id,
            notification.Status, outcome.RecipientCount, outcome.PushDeliveredCount);
    }
}
