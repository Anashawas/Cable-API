using Application.Billing;
using Application.Workers.WorkerAuth;
using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Workers;

/// <summary>
/// What a station's plan allows its workers (WORKER_TRACKER §2, §9; mapping confirmed 2026-11-03):
/// ManageStation / ViewFeedback always · PointOfSale with an active loyalty agreement or bundle · Connect keys with
/// "connect" · BiometricLogin with "biometrics" · everything else (wallet, offers, announcements, statistics,
/// attendance keys) with "workers" (premium and above). The worker limit is 1 without "workers", else unlimited.
/// </summary>
public sealed record StationPlanContext(string ProviderType, int ProviderId, IReadOnlySet<string> Features, bool HasLoyalty)
{
    public const string FeatureWorkers = "workers";
    public const string FeatureConnect = "connect";
    public const string FeatureBiometrics = "biometrics";

    public bool HasWorkers => Features.Contains(FeatureWorkers);

    /// <summary>How many workers the plan allows (int.MaxValue = unlimited).</summary>
    public int WorkerLimit => HasWorkers ? int.MaxValue : 1;

    /// <summary>null when the privilege works on this plan, else needsLoyalty | needsPremium | needsConnect.</summary>
    public string? LockReason(string privilege) => WorkerPrivileges.RequirementOf(privilege) switch
    {
        WorkerPrivileges.Requirement.None => null,
        WorkerPrivileges.Requirement.Loyalty => HasLoyalty ? null : "needsLoyalty",
        WorkerPrivileges.Requirement.Connect => Features.Contains(FeatureConnect) ? null : "needsConnect",
        WorkerPrivileges.Requirement.Biometrics => Features.Contains(FeatureBiometrics) ? null : "needsPremium",
        _ => HasWorkers ? null : "needsPremium",
    };

    /// <summary>granted ∩ allowed — what the worker can actually use.</summary>
    public IReadOnlyList<string> Effective(string? storedPrivileges) =>
        WorkerPrivileges.Parse(storedPrivileges).Where(k => LockReason(k) is null).ToList();

    /// <summary>Every key with its lock state, for the owner's privilege switches.</summary>
    public List<WorkerPrivilegeOption> Catalog() =>
        WorkerPrivileges.All.Select(k => new WorkerPrivilegeOption(k, LockReason(k) is not null, LockReason(k))).ToList();
}

public record WorkerPrivilegeOption(string Key, bool Locked, string? Reason);

public static class WorkerAccess
{
    /// <summary>The station's plan features (same calculation as GET /api/provider/subscription) and loyalty state.</summary>
    public static async Task<StationPlanContext> ContextAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var snapshot = await ProviderPlanResolver.LoadAsync(db, providerType, providerId, ct);
        var features = snapshot.Features().ToHashSet(StringComparer.Ordinal);

        var hasLoyalty = await db.PartnerAgreements.AsNoTracking()
            .AnyAsync(a => a.ProviderType == providerType && a.ProviderId == providerId && a.IsActive && !a.IsDeleted, ct);
        if (!hasLoyalty)
        {
            var bundle = await LoyaltyBundleService.LatestAsync(db, providerType, providerId, ct);
            hasLoyalty = bundle is not null && LoyaltyBundleService.IsUsable(bundle, DateTime.UtcNow);
        }

        return new StationPlanContext(providerType, providerId, features, hasLoyalty);
    }

    /// <summary>The owner's switch and the plan's suspension both allow the worker.</summary>
    public static bool IsUsable(ProviderManager pm) => pm.IsActive && pm.SuspendedByPlanAt is null && !pm.IsDeleted;
}

/// <summary>
/// Decision 38: when the plan allows one worker and more are active, one stays (KeepOnFree, else the oldest) and
/// the rest are suspended by the plan; when the plan allows them again they come back. Owner suspensions are
/// never touched. Runs from the billing watch (every 10 min) and after the owner's own changes.
/// </summary>
public static class WorkerPlanSync
{
    public static async Task<int> ApplyAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct,
        StationPlanContext? context = null)
    {
        context ??= await WorkerAccess.ContextAsync(db, providerType, providerId, ct);

        var rows = await db.ProviderManagers
            .Where(pm => pm.ProviderType == providerType && pm.ProviderId == providerId && !pm.IsDeleted)
            .OrderBy(pm => pm.CreatedAt).ThenBy(pm => pm.Id)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var changed = 0;
        var suspended = new List<int>();

        if (context.WorkerLimit == int.MaxValue)
        {
            foreach (var pm in rows.Where(r => r.SuspendedByPlanAt != null)) { pm.SuspendedByPlanAt = null; changed++; }
        }
        else
        {
            var eligible = rows.Where(r => r.IsActive).ToList();
            var keep = eligible.FirstOrDefault(r => r.KeepOnFree) ?? eligible.FirstOrDefault();
            foreach (var pm in rows)
            {
                var shouldSuspend = pm.IsActive && !ReferenceEquals(pm, keep);
                if (shouldSuspend && pm.SuspendedByPlanAt is null) { pm.SuspendedByPlanAt = now; suspended.Add(pm.UserId); changed++; }
                else if (!shouldSuspend && pm.SuspendedByPlanAt != null) { pm.SuspendedByPlanAt = null; changed++; }
            }
        }

        if (changed > 0) await db.SaveChanges(ct);
        foreach (var userId in suspended) await PartnerSessions.EndAllAsync(db, userId, ct);
        return changed;
    }

    /// <summary>Every provider that could be affected: more than one worker, or one still suspended by the plan.</summary>
    public static async Task<int> ApplyAllAsync(IApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        var providers = await db.ProviderManagers.AsNoTracking()
            .Where(pm => !pm.IsDeleted)
            .GroupBy(pm => new { pm.ProviderType, pm.ProviderId })
            .Where(g => g.Count() > 1 || g.Any(x => x.SuspendedByPlanAt != null))
            .Select(g => g.Key)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var p in providers)
        {
            try { changed += await ApplyAsync(db, p.ProviderType, p.ProviderId, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Worker plan sync failed for {Type} {Id}", p.ProviderType, p.ProviderId); }
        }
        return changed;
    }
}
