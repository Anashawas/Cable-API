using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Queries.GetWorkerByProvider;

public record WorkerDto(
    int       ProviderManagerId,
    int       UserId,
    string?   Name,
    string?   Phone,
    string?   Email,
    bool      IsActive,
    DateTime? AssignedAt
);

/// <summary>
/// Returns the single worker assigned to a provider, or null if none.
/// </summary>
public record GetWorkerByProviderRequest(string ProviderType, int ProviderId)
    : IRequest<WorkerDto?>;

public class GetWorkerByProviderQueryHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetWorkerByProviderRequest, WorkerDto?>
{
    public async Task<WorkerDto?> Handle(GetWorkerByProviderRequest request, CancellationToken cancellationToken)
    {
        return await applicationDbContext.ProviderManagers
            .AsNoTracking()
            .Where(pm => pm.ProviderType == request.ProviderType
                      && pm.ProviderId   == request.ProviderId
                      && !pm.IsDeleted)
            .Select(pm => new WorkerDto(
                pm.Id,
                pm.UserId,
                pm.User.Name,
                pm.User.Phone,
                pm.User.Email,
                pm.IsActive,
                pm.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
