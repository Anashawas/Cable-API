using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Commands.SetAutoApproveWorkerNotifications;

/// <summary>
/// F4: per-provider toggle — when enabled, a worker's fan announcement sends
/// directly instead of waiting in the approval queue. Owner or admin only
/// (a worker must not be able to unlock their own sends).
/// </summary>
public record SetAutoApproveWorkerNotificationsCommand(
    string ProviderType,
    int ProviderId,
    bool Enabled) : IRequest<bool>;

public class SetAutoApproveWorkerNotificationsCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SetAutoApproveWorkerNotificationsCommand, bool>
{
    public async Task<bool> Handle(SetAutoApproveWorkerNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");
        var isAdmin = await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"can not find ChargingPoint with id {request.ProviderId}");
            if (cp.OwnerId != userId && !isAdmin)
                throw new ForbiddenAccessException("Only the owner or an admin can change this setting.");
            cp.AutoApproveWorkerNotifications = request.Enabled;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"can not find ServiceProvider with id {request.ProviderId}");
            if (sp.OwnerId != userId && !isAdmin)
                throw new ForbiddenAccessException("Only the owner or an admin can change this setting.");
            sp.AutoApproveWorkerNotifications = request.Enabled;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        await applicationDbContext.SaveChanges(cancellationToken);
        return request.Enabled;
    }
}
