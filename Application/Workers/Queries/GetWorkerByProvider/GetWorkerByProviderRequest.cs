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
    DateTime? AssignedAt,
    /// <summary>Stored value (null = everything). Not for the UI — see Privileges / UsesAllPrivileges.</summary>
    string?   StoredPrivileges = null,
    /// <summary>When the worker first signed in to the partner app; null = "waiting for the first login".</summary>
    DateTime? FirstLoginAt = null,
    /// <summary>The worker still holds the temporary password.</summary>
    bool      MustChangePassword = false,
    DateTime? TempPasswordExpiresAt = null,
    /// <summary>Set while the login is locked after too many wrong passwords.</summary>
    DateTime? LockedUntil = null
)
{
    /// <summary>pendingFirstLogin | active | locked — the badge on the worker card.</summary>
    public string LoginStatus =>
        LockedUntil is DateTime l && l > DateTime.UtcNow ? "locked"
        : FirstLoginAt is null ? "pendingFirstLogin"
        : "active";

    /// <summary>What the worker may do, resolved (null stored = all).</summary>
    public IReadOnlyList<string> Privileges => Cable.Core.Constants.WorkerPrivileges.Parse(StoredPrivileges);
    public bool UsesAllPrivileges => Cable.Core.Constants.WorkerPrivileges.UsesAll(StoredPrivileges);

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
                pm.CreatedAt,
                pm.Privileges,
                applicationDbContext.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.FirstLoginAt).FirstOrDefault(),
                applicationDbContext.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.MustChangePassword).FirstOrDefault(),
                applicationDbContext.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.TempPasswordExpiresAt).FirstOrDefault(),
                applicationDbContext.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.LockedUntil).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
