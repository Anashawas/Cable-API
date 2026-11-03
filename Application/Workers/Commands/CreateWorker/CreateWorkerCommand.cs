using Cable.Core;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;
using Application.Common.Security;
using Application.Workers.WorkerAuth;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.Workers.Commands.CreateWorker;

/// <summary>
/// Owner adds a worker (WORKER_TRACKER §3.2.1, one request for the whole wizard). The worker logs in to the partner
/// app by phone with a temporary password the owner shares (§3.2.2); the server keeps only its hash.
/// One UserAccount per phone: a driver who becomes a worker keeps their account, cars, points and driver password —
/// only a partner credential and the assignment are added.
/// </summary>
public record CreateWorkerCommand(
    string ProviderType,
    int ProviderId,
    string Name,
    string Phone,
    string? Email = null,
    /// <summary>WorkerPrivileges keys the owner switched on; empty = station details only.</summary>
    List<string>? Privileges = null,
    /// <summary>Optional: the owner typed their own (8+ characters); otherwise one is generated.</summary>
    string? TempPassword = null
) : IRequest<CreateWorkerResult>;

public record CreateWorkerResult(
    int WorkerUserId,
    int ProviderManagerId,
    string TempPassword,
    DateTime TempPasswordExpiresAt,
    string ShareMessage,
    /// <summary>True when the phone already had a Cable account (a driver): nothing of theirs was changed.</summary>
    bool ExistingAccount);

public class CreateWorkerCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUserService,
    IPasswordHasher passwordHasher)
    : IRequestHandler<CreateWorkerCommand, CreateWorkerResult>
{
    public const string WorkerLimitReached = "WORKER_LIMIT_REACHED";
    public const string PhoneAlreadyAWorker = "PHONE_ALREADY_A_WORKER";
    public const string OwnerCannotBeWorker = "OWNER_CANNOT_BE_WORKER";
    public const string AccountNotEligible = "ACCOUNT_NOT_ELIGIBLE";

    public async Task<CreateWorkerResult> Handle(CreateWorkerCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        // Only the provider owner may add its worker.
        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, request.ProviderType, request.ProviderId, callerId, cancellationToken);

        // One worker per provider (the plan-based limit comes with the multi-worker step).
        var alreadyHasWorker = await db.ProviderManagers
            .AnyAsync(pm => pm.ProviderType == request.ProviderType && pm.ProviderId == request.ProviderId && !pm.IsDeleted, cancellationToken);
        if (alreadyHasWorker)
            throw new ConflictException(WorkerLimitReached,
                Lang.Pick("هاي المحطة عندها موظف — احذفه قبل ما تضيف غيره", "This provider already has a worker. Remove the current one before adding a new one."));

        var normalizedPhone = WorkerPhones.Normalize(request.Phone)
                              ?? throw new DataValidationException("Phone", Lang.Pick("رقم الموبايل غير صحيح", "Invalid phone number format."));

        var privileges = request.Privileges ?? [];
        var unknown = privileges.Where(p => !WorkerPrivileges.IsKnown(p)).ToList();
        if (unknown.Count > 0)
            throw new DataValidationException("Privileges", Lang.Pick("صلاحيات غير معروفة: ", "Unknown privileges: ") + string.Join(", ", unknown));

        var settings = await WorkerAuthSettings.ReadAsync(db, cancellationToken);

        // The phone's account, if any. One account per phone — never a second one.
        var worker = await db.UserAccounts.FirstOrDefaultAsync(u => u.Phone == normalizedPhone && !u.IsDeleted, cancellationToken);
        var existingAccount = worker is not null;

        if (worker is not null)
        {
            if (worker.RoleId == AdminRoleGuard.AdminRoleId)
                throw new ConflictException(AccountNotEligible, Lang.Pick("هذا الرقم ما بينفع يكون موظف", "This phone number cannot be added as a worker."));

            var isOwner = await db.ChargingPoints.AnyAsync(c => c.OwnerId == worker.Id && !c.IsDeleted, cancellationToken)
                          || await db.ServiceProviders.AnyAsync(s => s.OwnerId == worker.Id && !s.IsDeleted, cancellationToken);
            if (isOwner)
                throw new ConflictException(OwnerCannotBeWorker,
                    Lang.Pick("هذا الرقم صاحب محطة أو مزوّد خدمة على كيبل، وما بيقدر يكون موظف", "This phone number belongs to a station or provider owner on Cable and cannot be a worker."));

            // One station per worker (decision: no multi-station in v1).
            var isWorkerElsewhere = await db.ProviderManagers.AnyAsync(pm => pm.UserId == worker.Id && !pm.IsDeleted, cancellationToken);
            if (isWorkerElsewhere)
                throw new ConflictException(PhoneAlreadyAWorker,
                    Lang.Pick("هذا الرقم موظف عند شريك ثاني — تواصل مع دعم كيبل", "This phone number is already a worker at another partner — contact Cable support."));

            if (!worker.IsActive)
            {
                // A worker account we created earlier and then removed comes back; a driver the admin deactivated does not.
                var workerRoleId = await WorkerRoleIdAsync(cancellationToken);
                if (worker.RoleId != workerRoleId)
                    throw new ConflictException(AccountNotEligible, Lang.Pick("هذا الحساب موقوف — تواصل مع دعم كيبل", "This account is deactivated — contact Cable support."));
                worker.IsActive = true;
            }

            if (string.IsNullOrWhiteSpace(worker.Name)) worker.Name = request.Name.Trim();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var emailTaken = await db.UserAccounts.AnyAsync(u => !u.IsDeleted && u.Email == request.Email, cancellationToken);
                if (emailTaken)
                    throw new DataValidationException("Email", Lang.Pick("الإيميل مستعمل", "Email is already in use."));
            }

            // A new worker account: no driver password — the partner credential below is its only login.
            worker = new UserAccount
            {
                Name            = request.Name.Trim(),
                Email           = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                Phone           = normalizedPhone,
                Password        = null,
                RoleId          = await WorkerRoleIdAsync(cancellationToken),
                IsActive        = true,
                IsDeleted       = false,
                IsPhoneVerified = true,
                PhoneVerifiedAt = DateTime.UtcNow
            };
            db.UserAccounts.Add(worker);
            await db.SaveChanges(cancellationToken);
        }

        var (tempPassword, expiresAt) = await WorkerCredentials.IssueTempPasswordAsync(db, passwordHasher, settings, worker.Id, request.TempPassword, cancellationToken);

        var providerManager = new ProviderManager
        {
            ProviderType = request.ProviderType,
            ProviderId   = request.ProviderId,
            UserId       = worker.Id,
            IsActive     = true,
            // Always the explicit list: a key added later is never switched on by itself (§2, decision 16b).
            Privileges   = string.Join(",", WorkerPrivileges.All.Where(privileges.Contains))
        };
        db.ProviderManagers.Add(providerManager);
        await db.SaveChanges(cancellationToken);

        var (ownerName, providerName) = await WorkerCredentials.NamesAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        var message = WorkerShareMessage.Build(Lang.IsArabic ? "ar" : "en", worker.Name ?? request.Name, ownerName, providerName,
            WorkerPhones.ToLocal(normalizedPhone), tempPassword, expiresAt, settings.TempPasswordHours, settings.PartnerLoginUrl,
            hasBiometricLogin: false);

        return new CreateWorkerResult(worker.Id, providerManager.Id, tempPassword, expiresAt, message, existingAccount);
    }

    private async Task<int> WorkerRoleIdAsync(CancellationToken ct)
    {
        var id = await db.Roles.Where(r => r.Name == "Worker" && !r.IsDeleted).Select(r => r.Id).FirstOrDefaultAsync(ct);
        return id == 0 ? throw new CableApplicationException("Worker role not configured in the system") : id;
    }
}
