using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Links a worker <see cref="UserAccount"/> to a single provider
/// (a <see cref="ServiceProvider"/> or a <see cref="ChargingPoint"/>) that it is allowed
/// to manage alongside the owner. Polymorphic via <see cref="ProviderType"/> +
/// <see cref="ProviderId"/> — mirrors the existing ProviderOffer pattern.
///
/// Rules:
///  - At most ONE active worker per provider (enforced by a filtered unique index).
///  - The owner stays in CP/SP.OwnerId and is never a ProviderManager row.
///  - All workers have identical access (no access levels).
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

    /// <summary>Comma-separated WorkerPrivileges keys the owner granted; null = everything, "" = station details only.</summary>
    public string? Privileges  { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}
