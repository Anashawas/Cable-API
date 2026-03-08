using Application.Common.Interfaces;
using Application.NotificationInbox.Helpers;
using Cable.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.SendNotificationByFilter;

public record SendNotificationByCategoryCommand(
    int? CarTypeId,
    int? CarModelId,
    string? City,
    int NotificationTypeId,
    string Title,
    string Body,
    string? DeepLink = null,
    string? Data = null,
    FirebaseAppType AppType = FirebaseAppType.UserApp
) : IRequest<SendNotificationByFilterResult>;

public class SendNotificationByFilterCommandHandler(
    IApplicationDbContext applicationDbContext,
    INotificationService notificationService)
    : IRequestHandler<SendNotificationByCategoryCommand, SendNotificationByFilterResult>
{
    public async Task<SendNotificationByFilterResult> Handle(
        SendNotificationByCategoryCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Build query to find matching users
        var usersQuery = applicationDbContext.UserAccounts
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        // Filter by City if provided
        if (!string.IsNullOrWhiteSpace(request.City))
        {
            usersQuery = usersQuery.Where(x => x.City != null && x.City.ToLower() == request.City.ToLower());
        }


        if (request.CarTypeId.HasValue || request.CarModelId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.UserCars.Any(uc =>
                !uc.IsDeleted &&
                (!request.CarTypeId.HasValue || uc.CarModel.CarTypeId == request.CarTypeId.Value) &&
                (!request.CarModelId.HasValue || uc.CarModelId == request.CarModelId.Value)
            ));
        }


        var matchingUserIds = await usersQuery
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!matchingUserIds.Any())
        {
            return new SendNotificationByFilterResult
            {
                TargetedUsersCount = 0,
                NotificationsSentCount = 0,
                Message = "No users match the specified filters"
            };
        }
        
        // Get user tokens for the specified app type only
        var userTokens = await applicationDbContext.NotificationTokens
            .AsNoTracking()
            .Where(x => matchingUserIds.Contains(x.UserId) && x.AppType == request.AppType)
            .Select(x => new { x.UserId, x.Token })
            .ToListAsync(cancellationToken);

        var allTokens = userTokens.Select(x => x.Token).ToList();

        if (!allTokens.Any())
        {
            return new SendNotificationByFilterResult
            {
                TargetedUsersCount = matchingUserIds.Count,
                NotificationsSentCount = 0,
                Message = $"Found {matchingUserIds.Count} matching users, but none have registered FCM tokens for {request.AppType}"
            };
        }

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
            await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(
                applicationDbContext,
                successfulUserIds,
                request.NotificationTypeId,
                request.Title,
                request.Body,
                request.DeepLink,
                request.Data,
                cancellationToken);
        }

        return new SendNotificationByFilterResult
        {
            TargetedUsersCount = matchingUserIds.Count,
            NotificationsSentCount = successfulUserIds.Count,
            Message = $"Successfully sent notifications to {successfulUserIds.Count} out of {matchingUserIds.Count} matching users"
        };
    }
}

public class SendNotificationByFilterResult
{
    public int TargetedUsersCount { get; set; }
    public int NotificationsSentCount { get; set; }
    public string Message { get; set; } = string.Empty;
}
