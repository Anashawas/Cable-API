using Application.Common.Interfaces;
using Cable.Core.Utilities;
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
)
{
    // Returned in dial-ready E.164 (+962...) so tel: links work on the clients.
    // Storage is unchanged (962... in the DB).
    public string? Phone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(Phone);
}

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
