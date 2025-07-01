namespace Application.NotificationTokens.Commands.RegisterNotificationToken;

public record RegisterNotificationTokenCommand(
    int UserId,
    string Token,
    string OsName,
    string OsVersion,
    string AppVersion) : IRequest<int>;

public class RegisterNotificationTokenCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<RegisterNotificationTokenCommand, int>
{
    public async Task<int> Handle(RegisterNotificationTokenCommand request, CancellationToken cancellationToken)
    {
        var notificationToken = new NotificationToken()
        {
            UserId = request.UserId,
            Token = request.Token,
            DeviceId = Guid.CreateVersion7(),
            AppVersion = request.AppVersion,
            OsName = request.OsName,
            OsVersion = request.OsVersion,
        };
        applicationDbContext.NotificationTokens.Add(notificationToken);
        await applicationDbContext.SaveChanges(cancellationToken);
        return notificationToken.Id;
    }
}