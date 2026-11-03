using Application.Common.Interfaces;
using Application.Workers.WorkerAuth;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.CreateWorker;

/// <summary>
/// Owner adds a worker (WORKER_TRACKER §3.2.1, one request for the whole wizard). The worker logs in to the partner
/// app by phone with a temporary password the owner shares (§3.2.2); the server keeps only its hash.
/// One UserAccount per phone: a driver who becomes a worker keeps their account, cars, points and driver password —
/// only a partner credential and the assignment are added. The station's plan sets the worker limit.
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
    bool ExistingAccount,
    /// <summary>Granted keys the station's plan does not allow right now (shown locked to the owner).</summary>
    IReadOnlyList<string> LockedPrivileges);

public class CreateWorkerCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUserService,
    IPasswordHasher passwordHasher)
    : IRequestHandler<CreateWorkerCommand, CreateWorkerResult>
{
    public async Task<CreateWorkerResult> Handle(CreateWorkerCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        // Only the provider owner may add its worker.
        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, request.ProviderType, request.ProviderId, callerId, cancellationToken);

        var context = await WorkerAccess.ContextAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        if (await WorkerEligibility.LimitReasonAsync(db, request.ProviderType, request.ProviderId, cancellationToken, context) is { } limit)
            throw new ConflictException(limit.Code, limit.Message);

        var privileges = request.Privileges ?? [];
        var unknown = privileges.Where(p => !WorkerPrivileges.IsKnown(p)).ToList();
        if (unknown.Count > 0)
            throw new DataValidationException("Privileges", Lang.Pick("صلاحيات غير معروفة: ", "Unknown privileges: ") + string.Join(", ", unknown));

        var (phoneReason, worker) = await WorkerEligibility.PhoneReasonAndAccountAsync(db, request.Phone, cancellationToken);
        if (phoneReason is { } reason)
        {
            if (reason.Code == WorkerEligibility.InvalidPhone) throw new DataValidationException("Phone", reason.Message);
            throw new ConflictException(reason.Code, reason.Message);
        }
        var normalizedPhone = WorkerPhones.Normalize(request.Phone)!;
        var existingAccount = worker is not null;

        var settings = await WorkerAuthSettings.ReadAsync(db, cancellationToken);

        if (worker is not null)
        {
            worker.IsActive = true;   // a removed wizard-created worker comes back (eligibility refused other inactive accounts)
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
            Privileges   = WorkerPrivileges.Format(privileges)
        };
        db.ProviderManagers.Add(providerManager);
        await db.SaveChanges(cancellationToken);

        var effective = context.Effective(providerManager.Privileges);
        var locked = WorkerPrivileges.Parse(providerManager.Privileges).Where(k => !effective.Contains(k, StringComparer.Ordinal)).ToList();

        var (ownerName, providerName) = await WorkerCredentials.NamesAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        var message = WorkerShareMessage.Build(Lang.IsArabic ? "ar" : "en", worker.Name ?? request.Name, ownerName, providerName,
            WorkerPhones.ToLocal(normalizedPhone), tempPassword, expiresAt, settings.TempPasswordHours, settings.PartnerLoginUrl,
            hasBiometricLogin: effective.Contains(WorkerPrivileges.BiometricLogin, StringComparer.Ordinal));

        return new CreateWorkerResult(worker.Id, providerManager.Id, tempPassword, expiresAt, message, existingAccount, locked);
    }

    private async Task<int> WorkerRoleIdAsync(CancellationToken ct)
    {
        var id = await db.Roles.Where(r => r.Name == "Worker" && !r.IsDeleted).Select(r => r.Id).FirstOrDefaultAsync(ct);
        return id == 0 ? throw new CableApplicationException("Worker role not configured in the system") : id;
    }
}
