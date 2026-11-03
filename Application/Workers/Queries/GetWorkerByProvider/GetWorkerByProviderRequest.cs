using Cable.Core;
using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Queries.GetWorkerByProvider;

public record WorkerDto(
    int       ProviderManagerId,
    int       UserId,
    string?   Name,
    string?   Phone,
    string?   Email,
    /// <summary>The owner's switch.</summary>
    bool      IsActive,
    DateTime? AssignedAt,
    /// <summary>Stored value (null = the legacy set). Not for the UI — see Privileges / EffectivePrivileges.</summary>
    string?   StoredPrivileges = null,
    /// <summary>When the worker first signed in to the partner app; null = "waiting for the first login".</summary>
    DateTime? FirstLoginAt = null,
    /// <summary>The worker still holds the temporary password.</summary>
    bool      MustChangePassword = false,
    DateTime? TempPasswordExpiresAt = null,
    /// <summary>Set while the login is locked after too many wrong passwords.</summary>
    DateTime? LockedUntil = null,
    /// <summary>Set while the station's plan allows fewer workers than are active (decision 38).</summary>
    DateTime? SuspendedByPlanAt = null,
    /// <summary>The owner's choice of who stays when the plan drops to one worker.</summary>
    bool      KeepOnFree = false
)
{
    /// <summary>pendingFirstLogin | active | locked — the badge on the worker card.</summary>
    public string LoginStatus =>
        LockedUntil is DateTime l && l > DateTime.UtcNow ? "locked"
        : FirstLoginAt is null ? "pendingFirstLogin"
        : "active";

    /// <summary>active | suspended (by the owner) | suspendedByPlan.</summary>
    public string Status => !IsActive ? "suspended" : SuspendedByPlanAt is not null ? "suspendedByPlan" : "active";

    /// <summary>What the owner granted.</summary>
    public IReadOnlyList<string> Privileges => Cable.Core.Constants.WorkerPrivileges.Parse(StoredPrivileges);
    public bool UsesAllPrivileges => Cable.Core.Constants.WorkerPrivileges.UsesAll(StoredPrivileges);

    /// <summary>What the worker can actually use on the station's plan (granted ∩ allowed). Filled by the handlers.</summary>
    public IReadOnlyList<string> EffectivePrivileges { get; init; } = [];

    // Returned in dial-ready E.164 (+962...) so tel: links work on the clients.
    // Storage is unchanged (962... in the DB).
    public string? Phone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(Phone);
}

/// <summary>The one projection every worker list / card uses.</summary>
public static class WorkerDtoQuery
{
    public static IQueryable<WorkerDto> Project(IQueryable<ProviderManager> rows, IApplicationDbContext db) =>
        rows.Select(pm => new WorkerDto(
            pm.Id,
            pm.UserId,
            pm.User.Name,
            pm.User.Phone,
            pm.User.Email,
            pm.IsActive,
            pm.CreatedAt,
            pm.Privileges,
            db.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.FirstLoginAt).FirstOrDefault(),
            db.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.MustChangePassword).FirstOrDefault(),
            db.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.TempPasswordExpiresAt).FirstOrDefault(),
            db.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted).Select(c => c.LockedUntil).FirstOrDefault(),
            pm.SuspendedByPlanAt,
            pm.KeepOnFree));

    /// <summary>The provider's workers with their effective privileges, oldest first.</summary>
    public static async Task<List<WorkerDto>> ListAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct,
        StationPlanContext? context = null)
    {
        context ??= await WorkerAccess.ContextAsync(db, providerType, providerId, ct);
        var rows = await Project(db.ProviderManagers.AsNoTracking()
                .Where(pm => pm.ProviderType == providerType && pm.ProviderId == providerId && !pm.IsDeleted)
                .OrderBy(pm => pm.CreatedAt).ThenBy(pm => pm.Id), db)
            .ToListAsync(ct);
        return rows.Select(w => w with { EffectivePrivileges = context.Effective(w.StoredPrivileges) }).ToList();
    }

    public static async Task<WorkerDto> OneAsync(IApplicationDbContext db, int providerManagerId, CancellationToken ct)
    {
        var w = await Project(db.ProviderManagers.AsNoTracking().Where(pm => pm.Id == providerManagerId && !pm.IsDeleted), db).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException($"Worker assignment {providerManagerId} not found");
        var pm = await db.ProviderManagers.AsNoTracking().Where(x => x.Id == providerManagerId).Select(x => new { x.ProviderType, x.ProviderId }).FirstAsync(ct);
        var context = await WorkerAccess.ContextAsync(db, pm.ProviderType, pm.ProviderId, ct);
        return w with { EffectivePrivileges = context.Effective(w.StoredPrivileges) };
    }

    /// <summary>Owner of the provider or an admin.</summary>
    public static async Task EnsureOwnerOrAdminAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId, CancellationToken ct)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct)) return;
        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, providerType, providerId, callerId, ct);
    }
}

/// <summary>The workers of a provider (owner or admin). Replaces the single-worker answer of the old GET.</summary>
public record GetWorkersByProviderRequest(string ProviderType, int ProviderId) : IRequest<List<WorkerDto>>;

public class GetWorkersByProviderQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetWorkersByProviderRequest, List<WorkerDto>>
{
    public async Task<List<WorkerDto>> Handle(GetWorkersByProviderRequest request, CancellationToken cancellationToken)
    {
        await WorkerDtoQuery.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, cancellationToken);
        return await WorkerDtoQuery.ListAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
    }
}

/// <summary>The privilege keys with their lock state on this station's plan (owner or admin); the owner's switches.</summary>
public record GetWorkerPrivilegeCatalogRequest(string ProviderType, int ProviderId) : IRequest<List<WorkerPrivilegeOption>>;

public class GetWorkerPrivilegeCatalogRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetWorkerPrivilegeCatalogRequest, List<WorkerPrivilegeOption>>
{
    public async Task<List<WorkerPrivilegeOption>> Handle(GetWorkerPrivilegeCatalogRequest request, CancellationToken cancellationToken)
    {
        await WorkerDtoQuery.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, cancellationToken);
        var context = await WorkerAccess.ContextAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        return context.Catalog();
    }
}

/// <summary>
/// GET /api/workers/me — the worker's own station and effective privileges (§2: "the server returns the worker the
/// effective privileges"). The partner app polls it every minute; a suspended worker sees it here before the next 401.
/// </summary>
public record GetMyWorkerAssignmentRequest : IRequest<WorkerSelfDto>;

public record WorkerSelfDto(
    int ProviderManagerId,
    string ProviderType,
    int ProviderId,
    string ProviderName,
    /// <summary>active | suspended | suspendedByPlan.</summary>
    string Status,
    /// <summary>granted ∩ allowed by the station's plan — switch the tabs on this.</summary>
    IReadOnlyList<string> Privileges,
    /// <summary>What the owner granted but the plan does not allow right now.</summary>
    IReadOnlyList<string> LockedPrivileges,
    bool MustChangePassword);

public class GetMyWorkerAssignmentRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyWorkerAssignmentRequest, WorkerSelfDto>
{
    public const string NotAWorker = "NOT_A_WORKER";

    public async Task<WorkerSelfDto> Handle(GetMyWorkerAssignmentRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var pm = await db.ProviderManagers.AsNoTracking()
                     .Where(x => x.UserId == userId && !x.IsDeleted)
                     .OrderByDescending(x => x.IsActive).ThenBy(x => x.CreatedAt)
                     .Select(x => new { x.Id, x.ProviderType, x.ProviderId, x.IsActive, x.SuspendedByPlanAt, x.Privileges })
                     .FirstOrDefaultAsync(cancellationToken)
                 ?? throw new CodedNotFoundException(NotAWorker, Lang.Pick("حسابك مش موظف عند أي شريك", "This account is not a worker at any partner"));

        var providerName = pm.ProviderType == WorkerOwnershipHelper.ChargingPoint
            ? await db.ChargingPoints.AsNoTracking().Where(c => c.Id == pm.ProviderId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : await db.ServiceProviders.AsNoTracking().Where(s => s.Id == pm.ProviderId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);

        var context = await WorkerAccess.ContextAsync(db, pm.ProviderType, pm.ProviderId, cancellationToken);
        var granted = Cable.Core.Constants.WorkerPrivileges.Parse(pm.Privileges);
        var effective = context.Effective(pm.Privileges);
        var mustChange = await db.PartnerCredentials.AsNoTracking().AnyAsync(c => c.UserId == userId && !c.IsDeleted && c.MustChangePassword, cancellationToken);

        var status = !pm.IsActive ? "suspended" : pm.SuspendedByPlanAt is not null ? "suspendedByPlan" : "active";
        return new WorkerSelfDto(pm.Id, pm.ProviderType, pm.ProviderId, providerName ?? "", status,
            effective, granted.Where(k => !effective.Contains(k, StringComparer.Ordinal)).ToList(), mustChange);
    }
}

/// <summary>The wizard's step 1: can this phone be added here? Says nothing about whether the phone has a Cable account.</summary>
public record CheckWorkerPhoneRequest(string ProviderType, int ProviderId, string Phone) : IRequest<WorkerPhoneCheckDto>;

public record WorkerPhoneCheckDto(
    bool CanAdd,
    /// <summary>null, or PHONE_ALREADY_A_WORKER | OWNER_CANNOT_BE_WORKER | ACCOUNT_NOT_ELIGIBLE | WORKER_LIMIT_REACHED | INVALID_PHONE.</summary>
    string? Reason,
    string? Message);

public class CheckWorkerPhoneRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CheckWorkerPhoneRequest, WorkerPhoneCheckDto>
{
    public async Task<WorkerPhoneCheckDto> Handle(CheckWorkerPhoneRequest request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, request.ProviderType, request.ProviderId, callerId, cancellationToken);

        var limit = await Commands.CreateWorker.WorkerEligibility.LimitReasonAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        if (limit is not null) return new WorkerPhoneCheckDto(false, limit.Value.Code, limit.Value.Message);

        var phone = await Commands.CreateWorker.WorkerEligibility.PhoneReasonAsync(db, request.Phone, cancellationToken);
        return phone is null ? new WorkerPhoneCheckDto(true, null, null) : new WorkerPhoneCheckDto(false, phone.Value.Code, phone.Value.Message);
    }
}
