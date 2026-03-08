using Application.Common.Interfaces;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationTokens.Commands.RefreshNotificationToken;

public record RefreshNotificationTokenCommand(
    string Token,
    string OsName,
    string OsVersion,
    string AppVersion,
    FirebaseAppType AppType = FirebaseAppType.UserApp
) : IRequest;

public record RefreshNotificationTokenCommandHandler(
    IApplicationDbContext ApplicationDbContext,
    ICurrentUserService CurrentUserService)
    : IRequestHandler<RefreshNotificationTokenCommand>
{
    public async Task Handle(RefreshNotificationTokenCommand request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserService.UserId
            ?? throw new UnauthorizedAccessException("User is not authenticated");

        var user = await ApplicationDbContext.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == userId, cancellationToken)
            ?? throw new NotFoundException($"Can not find user with id {userId}");

        // Query by BOTH UserId AND AppType (composite unique key)
        var existingToken = await ApplicationDbContext.NotificationTokens
            .FirstOrDefaultAsync(x => x.UserId == userId && x.AppType == request.AppType, cancellationToken);

        if (existingToken != null)
        {
            // Update existing token for this app type
            existingToken.Token = request.Token;
            existingToken.OsName = request.OsName;
            existingToken.OsVersion = request.OsVersion;
            existingToken.AppVersion = request.AppVersion;
            existingToken.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new token for this app type
            var now = DateTime.UtcNow;
            var newToken = new NotificationToken
            {
                UserId = userId,
                Token = request.Token,
                OsName = request.OsName,
                OsVersion = request.OsVersion,
                AppVersion = request.AppVersion,
                AppType = request.AppType,
                CreatedAt = now,
                UpdatedAt = now
            };
            ApplicationDbContext.NotificationTokens.Add(newToken);
        }

        await ApplicationDbContext.SaveChanges(cancellationToken);
    }
}