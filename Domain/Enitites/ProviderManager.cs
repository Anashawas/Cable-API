using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Links a worker <see cref="UserAccount"/> to a single provider
/// (a <see cref="ServiceProvider"/> or a <see cref="ChargingPoint"/>) that it is allowed
/// to manage alongside the owner. Polymorphic via <see cref="ProviderType"/> +
/// <see cref="ProviderId"/> — mirrors the existing ProviderOffer pattern.
///
/// Rules:
///  - Several workers per provider; the station's plan sets the limit (1 on free, unlimited with the
///    "workers" feature). One station per worker (one row per user).
///  - The owner stays in CP/SP.OwnerId and is never a ProviderManager row.
///  - A worker is usable when IsActive (the owner's switch) and SuspendedByPlanAt is null (the plan's).
/// </summary>
public class ProviderManager : BaseAuditableEntity
{
    /// <summary>"ServiceProvider" or "ChargingPoint".</summary>
    public string ProviderType { get; set; } = null!;

    /// <summary>The Id of the ServiceProvider or ChargingPoint this worker manages.</summary>
    public int    ProviderId   { get; set; }

    /// <summary>The worker's UserAccount Id (RoleId = Worker).</summary>
    public int    UserId       { get; set; }

    /// <summary>Owner can pause/resume the worker without removing the record.</summary>
    public bool   IsActive     { get; set; } = true;

    /// <summary>Comma-separated WorkerPrivileges keys the owner granted (always explicit); null = the legacy set, "" = station details only.</summary>
    public string? Privileges  { get; set; }

    /// <summary>
    /// UTC. Set by the plan sync when the station's plan allows fewer workers than are active (decision 38):
    /// the worker cannot sign in until the plan allows them again. Distinct from the owner's IsActive.
    /// </summary>
    public DateTime? SuspendedByPlanAt { get; set; }

    /// <summary>The owner's choice of which worker stays when the plan drops to one (default: the oldest active).</summary>
    public bool KeepOnFree { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}
