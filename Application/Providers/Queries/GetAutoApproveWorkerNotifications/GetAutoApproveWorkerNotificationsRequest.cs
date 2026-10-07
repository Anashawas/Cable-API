using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Queries.GetAutoApproveWorkerNotifications;

public record AutoApproveWorkerNotificationsDto(string ProviderType, int ProviderId, bool Enabled);

/// <summary>
/// Current state of the F4 toggle. The setter has existed since F4 shipped, but
/// nothing exposed the value — a client could turn it on or off without ever
/// being able to show which way it was set. Same owner/admin restriction as the
/// setter: a worker must not learn whether their sends bypass approval.
/// </summary>
public record GetAutoApproveWorkerNotificationsRequest(string ProviderType, int ProviderId)
    : IRequest<AutoApproveWorkerNotificationsDto>;

public class GetAutoApproveWorkerNotificationsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAutoApproveWorkerNotificationsRequest, AutoApproveWorkerNotificationsDto>
{
    public async Task<AutoApproveWorkerNotificationsDto> Handle(
        GetAutoApproveWorkerNotificationsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");
        var isAdmin = await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        bool enabled;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .AsNoTracking()
                         .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                         .Select(x => new { x.OwnerId, x.AutoApproveWorkerNotifications })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException($"can not find ChargingPoint with id {request.ProviderId}");

            if (cp.OwnerId != userId && !isAdmin)
                throw new ForbiddenAccessException("Only the owner or an admin can read this setting.");

            enabled = cp.AutoApproveWorkerNotifications;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .AsNoTracking()
                         .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                         .Select(x => new { x.OwnerId, x.AutoApproveWorkerNotifications })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException($"can not find ServiceProvider with id {request.ProviderId}");

            if (sp.OwnerId != userId && !isAdmin)
                throw new ForbiddenAccessException("Only the owner or an admin can read this setting.");

            enabled = sp.AutoApproveWorkerNotifications;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        return new AutoApproveWorkerNotificationsDto(request.ProviderType, request.ProviderId, enabled);
    }
}
