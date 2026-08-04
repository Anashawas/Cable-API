using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Providers.Commands.SendFavoritesNotification;

public record SendFavoritesNotificationResult(
    int RecipientCount,
    int PushDeliveredCount,
    int RemainingSendsToday,
    string Status,          // "sent" | "pending" (F3: worker submissions await owner approval)
    int NotificationId);

/// <summary>
/// Provider announcement to everyone who favorited their charging point /
/// service business. Owner, worker, or admin only. F5: send a
/// notificationTypeId and the title is built server-side
/// ("{typeName} From {stationName}"); a raw title is still accepted.
/// F3: a WORKER's send is stored as pending until the owner/admin approves —
/// unless the provider has autoApproveWorkerNotifications on (F4).
/// Routing (deepLink + FCM type/chargerId) is attached automatically (R3).
/// </summary>
public record SendFavoritesNotificationCommand(
    string ProviderType,
    int ProviderId,
    string? Title,
    string Body,
    int? NotificationTypeId = null
) : IRequest<SendFavoritesNotificationResult>;

public class SendFavoritesNotificationCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    INotificationService notificationService,
    ILogger<SendFavoritesNotificationCommandHandler> logger)
    : IRequestHandler<SendFavoritesNotificationCommand, SendFavoritesNotificationResult>
{
    public async Task<SendFavoritesNotificationResult> Handle(SendFavoritesNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var provider = await FavoritesNotificationDelivery.ResolveProviderAsync(
            applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);

        // Owner, active worker, or admin.
        var isWorker = await applicationDbContext.ProviderManagers.AsNoTracking()
            .AnyAsync(pm => pm.ProviderType == request.ProviderType
                            && pm.ProviderId == request.ProviderId
                            && pm.UserId == userId
                            && pm.IsActive && !pm.IsDeleted, cancellationToken);
        var isAdmin = await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var isOwner = provider.OwnerId == userId;

        if (!isOwner && !isWorker && !isAdmin)
            throw new ForbiddenAccessException("Only the provider owner, its worker, or an admin can notify its favorites.");

        // F5: resolve the inbox type + server-built title.
        var typeId = request.NotificationTypeId;
        string? typeName = null;
        if (typeId.HasValue)
        {
            // DECISION-2: the Arabic display name feeds the title; code name is the fallback.
            var type = await applicationDbContext.NotificationTypes.AsNoTracking()
                           .Where(t => t.Id == typeId.Value)
                           .Select(t => new { t.Name, t.NameAr })
                           .FirstOrDefaultAsync(cancellationToken)
                       ?? throw new NotFoundException($"can not find notification type with id {typeId}");
            typeName = !string.IsNullOrWhiteSpace(type.NameAr) ? type.NameAr : type.Name;
        }
        else
        {
            typeId = await applicationDbContext.NotificationTypes.AsNoTracking()
                .Where(t => t.Name == FavoritesNotificationDelivery.AnnouncementTypeName)
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? FavoritesNotificationDelivery.FallbackTypeId;
        }

        var title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title!
            : typeName is not null
                ? FavoritesNotificationDelivery.BuildTitle(typeName, provider.Name)
                : throw new DataValidationException("Title", "Send a title or a notificationTypeId (the title is then built server-side).");

        var recipientIds = await FavoritesNotificationDelivery.GetRecipientIdsAsync(
            applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);
        if (recipientIds.Count == 0)
            throw new DataValidationException("Favorites", "No users have favorited this provider yet.");

        var sentToday = await FavoritesNotificationDelivery.CountSentLast24HoursAsync(
            applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);

        // F3: a worker's send waits for owner approval (unless auto-approve, F4).
        if (isWorker && !isOwner && !isAdmin && !provider.AutoApproveWorkerNotifications)
        {
            var pending = new ProviderFavoriteNotification
            {
                ProviderType = request.ProviderType,
                ProviderId = request.ProviderId,
                SentByUserId = userId,
                Title = title,
                Body = request.Body,
                RecipientCount = recipientIds.Count, // snapshot; refreshed at approval time
                Status = FavoritesNotificationDelivery.StatusPending,
                NotificationTypeId = typeId
            };
            applicationDbContext.ProviderFavoriteNotifications.Add(pending);
            await applicationDbContext.SaveChanges(cancellationToken);

            return new SendFavoritesNotificationResult(
                recipientIds.Count, 0,
                Math.Max(0, FavoritesNotificationDelivery.MaxSendsPerDay - sentToday),
                FavoritesNotificationDelivery.StatusPending, pending.Id);
        }

        // Direct send path — the rate limit bites on ACTUAL sends only.
        if (sentToday >= FavoritesNotificationDelivery.MaxSendsPerDay)
            throw new DataValidationException("Limit",
                $"This provider already sent {FavoritesNotificationDelivery.MaxSendsPerDay} notifications in the last 24 hours. Try again later.");

        var outcome = await FavoritesNotificationDelivery.DeliverAsync(
            applicationDbContext, notificationService, logger,
            request.ProviderType, request.ProviderId, provider.Name,
            title, request.Body, typeId.Value, recipientIds, cancellationToken);

        var record = new ProviderFavoriteNotification
        {
            ProviderType = request.ProviderType,
            ProviderId = request.ProviderId,
            SentByUserId = userId,
            Title = title,
            Body = request.Body,
            RecipientCount = outcome.RecipientCount,
            Status = FavoritesNotificationDelivery.StatusSent,
            NotificationTypeId = typeId,
            BatchId = outcome.BatchId,
            DeliveredCount = outcome.PushDeliveredCount,
            SentAt = DateTime.UtcNow
        };
        applicationDbContext.ProviderFavoriteNotifications.Add(record);
        await applicationDbContext.SaveChanges(cancellationToken);

        return new SendFavoritesNotificationResult(
            outcome.RecipientCount, outcome.PushDeliveredCount,
            FavoritesNotificationDelivery.MaxSendsPerDay - sentToday - 1,
            FavoritesNotificationDelivery.StatusSent, record.Id);
    }
}
