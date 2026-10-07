using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A condition the alert job found and told people about: a charger offline too long, a
/// plug Faulted too long, a session open too long. One open row per (type, target) at a
/// time — the job creates it when the condition first exceeds its threshold, notifies,
/// and closes it (ResolvedAt) when the condition clears. History stays for the reports.
/// </summary>
public class OcppAlert : BaseAuditableEntity
{
    public int OcppChargePointId { get; set; }

    /// <summary>See OcppAlertType: ChargerOffline | ConnectorFaulted | SessionTooLong.</summary>
    public string Type { get; set; } = null!;

    /// <summary>The plug for ConnectorFaulted / SessionTooLong; null for charger-level alerts.</summary>
    public int? ConnectorId { get; set; }

    /// <summary>The session for SessionTooLong.</summary>
    public int? OcppTransactionId { get; set; }

    /// <summary>When the condition began (DisconnectedAt, StatusUpdatedAt, StartedAt) — not when we noticed.</summary>
    public DateTime ConditionSince { get; set; }

    public DateTime NotifiedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Short context: the error code, the idTag, the kWh so far.</summary>
    public string? Details { get; set; }

    /// <summary>How many people got the push + inbox entry.</summary>
    public int Recipients { get; set; }

    public virtual OcppChargePoint ChargePoint { get; set; } = null!;
}
