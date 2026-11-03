using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Workers.WorkerAuth;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.CreateWorker;

/// <summary>The refusal rules of adding a worker, shared by POST /api/workers and GET /api/workers/check-phone.</summary>
public static class WorkerEligibility
{
    public const string WorkerLimitReached = "WORKER_LIMIT_REACHED";
    public const string PhoneAlreadyAWorker = "PHONE_ALREADY_A_WORKER";
    public const string OwnerCannotBeWorker = "OWNER_CANNOT_BE_WORKER";
    public const string AccountNotEligible = "ACCOUNT_NOT_ELIGIBLE";
    public const string InvalidPhone = "INVALID_PHONE";

    /// <summary>1 worker without the "workers" feature, unlimited with it (decision 1 / 17).</summary>
    public static async Task<(string Code, string Message)?> LimitReasonAsync(IApplicationDbContext db, string providerType, int providerId,
        CancellationToken ct, StationPlanContext? context = null)
    {
        context ??= await WorkerAccess.ContextAsync(db, providerType, providerId, ct);
        var count = await db.ProviderManagers.CountAsync(pm => pm.ProviderType == providerType && pm.ProviderId == providerId && !pm.IsDeleted, ct);
        if (count < context.WorkerLimit) return null;
        return (WorkerLimitReached, context.HasWorkers
            ? Lang.Pick("وصلت الحد الأقصى للموظفين", "The worker limit has been reached")
            : Lang.Pick("الباقة المجانية بتسمح بموظف واحد — بالباقة المميّزة بدون حد", "The free plan allows one worker — unlimited on Premium"));
    }

    /// <summary>Why this phone cannot become a worker, or null. The account behind it, when any, is returned by the other overload.</summary>
    public static async Task<(string Code, string Message)?> PhoneReasonAsync(IApplicationDbContext db, string phone, CancellationToken ct)
    {
        var (reason, _) = await PhoneReasonAndAccountAsync(db, phone, ct);
        return reason;
    }

    public static async Task<((string Code, string Message)? Reason, UserAccount? Account)> PhoneReasonAndAccountAsync(IApplicationDbContext db, string phone, CancellationToken ct)
    {
        var normalized = WorkerPhones.Normalize(phone);
        if (normalized is null)
            return ((InvalidPhone, Lang.Pick("رقم الموبايل غير صحيح", "Invalid phone number format.")), null);

        var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.Phone == normalized && !u.IsDeleted, ct);
        if (account is null) return (null, null);

        if (account.RoleId == AdminRoleGuard.AdminRoleId)
            return ((AccountNotEligible, Lang.Pick("هذا الرقم ما بينفع يكون موظف", "This phone number cannot be added as a worker.")), account);

        var isOwner = await db.ChargingPoints.AnyAsync(c => c.OwnerId == account.Id && !c.IsDeleted, ct)
                      || await db.ServiceProviders.AnyAsync(s => s.OwnerId == account.Id && !s.IsDeleted, ct);
        if (isOwner)
            return ((OwnerCannotBeWorker, Lang.Pick("هذا الرقم صاحب محطة أو مزوّد خدمة على كيبل، وما بيقدر يكون موظف",
                "This phone number belongs to a station or provider owner on Cable and cannot be a worker.")), account);

        // One station per worker (decision: no multi-station in v1).
        if (await db.ProviderManagers.AnyAsync(pm => pm.UserId == account.Id && !pm.IsDeleted, ct))
            return ((PhoneAlreadyAWorker, Lang.Pick("هذا الرقم موظف عند شريك ثاني — تواصل مع دعم كيبل",
                "This phone number is already a worker at another partner — contact Cable support.")), account);

        if (!account.IsActive)
        {
            // A worker account we created earlier and then removed comes back; a driver the admin deactivated does not.
            var workerRoleId = await db.Roles.Where(r => r.Name == "Worker" && !r.IsDeleted).Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (account.RoleId != workerRoleId)
                return ((AccountNotEligible, Lang.Pick("هذا الحساب موقوف — تواصل مع دعم كيبل", "This account is deactivated — contact Cable support.")), account);
        }

        return (null, account);
    }
}
