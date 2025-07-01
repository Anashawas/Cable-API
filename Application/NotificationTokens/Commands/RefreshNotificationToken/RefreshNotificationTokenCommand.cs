using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationTokens.Commands.RefreshNotificationToken;

public record RefreshNotificationTokenCommand(int UserId, Guid DeviceId, string Token) : IRequest;

public record RefreshNotificationTokenCommandHandler(IApplicationDbContext ApplicationDbContext)
    : IRequestHandler<RefreshNotificationTokenCommand>
{
    public async Task Handle(RefreshNotificationTokenCommand request, CancellationToken cancellationToken)
    {
        var notificationToken =
            await ApplicationDbContext.NotificationTokens.FirstOrDefaultAsync(
                x => x.UserId == request.UserId && x.DeviceId == request.DeviceId, cancellationToken) ??
            throw new NotFoundException($"Can not find notification token for user with id {request.UserId}.");
        notificationToken.Token = request.Token;
        await ApplicationDbContext.SaveChanges(cancellationToken);
    }
}