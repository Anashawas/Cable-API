using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Application.Providers.Commands.SendFavoritesNotification;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Queries.GetFavoritesNotificationHistory;

public record FavoritesNotificationHistoryDto(
    int Id,
    string Title,
    string Body,
    string Status,          // "pending" | "sent" | "rejected"
    int? NotificationTypeId,
    string? NotificationTypeName,
    int SentByUserId,
    string? SentByName,
    DateTime SubmittedAt,
    DateTime? SentAt,
    DateTime? DecidedAt,
    int RecipientCount,
    int? DeliveredCount,
    int ReadCount);

/// <summary>
/// F1: a partner's announcement history for one asset — "reached N users,
/// read by M". Includes pending/rejected worker submissions so the owner sees
/// the whole story. Owner, worker, or admin. Set PendingOnly for the F3
/// approval queue.
/// </summary>
public record GetFavoritesNotificationHistoryRequest(
    string ProviderType,
    int ProviderId,
    bool PendingOnly = false,
    int? Page = null,
    int? PageSize = null)
    : IRequest<PagedResult<FavoritesNotificationHistoryDto>>;

public class GetFavoritesNotificationHistoryRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetFavoritesNotificationHistoryRequest, PagedResult<FavoritesNotificationHistoryDto>>
{
    public async Task<PagedResult<FavoritesNotificationHistoryDto>> Handle(
        GetFavoritesNotificationHistoryRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var provider = await FavoritesNotificationDelivery.ResolveProviderAsync(
            applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);

        var isWorker = await applicationDbContext.ProviderManagers.AsNoTracking()
            .AnyAsync(pm => pm.ProviderType == request.ProviderType
                            && pm.ProviderId == request.ProviderId
                            && pm.UserId == userId
                            && pm.IsActive && !pm.IsDeleted, cancellationToken);

        if (provider.OwnerId != userId && !isWorker
            && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
            throw new ForbiddenAccessException("Only the provider owner, its worker, or an admin can view its notifications.");

        var query = applicationDbContext.ProviderFavoriteNotifications.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.ProviderType == request.ProviderType
                        && x.ProviderId == request.ProviderId);
        if (request.PendingOnly)
            query = query.Where(x => x.Status == FavoritesNotificationDelivery.StatusPending);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FavoritesNotificationHistoryDto(
                x.Id,
                x.Title,
                x.Body,
                x.Status,
                x.NotificationTypeId,
                x.NotificationTypeId != null
                    ? applicationDbContext.NotificationTypes
                        .Where(t => t.Id == x.NotificationTypeId)
                        .Select(t => t.Name)
                        .FirstOrDefault()
                    : null,
                x.SentByUserId,
                x.SentBy.Name,
                x.CreatedAt,
                x.SentAt,
                x.DecidedAt,
                x.RecipientCount,
                x.DeliveredCount,
                x.BatchId != null
                    ? applicationDbContext.NotificationInboxes
                        .Count(n => n.BatchId == x.BatchId && n.IsRead && !n.IsDeleted)
                    : 0))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
