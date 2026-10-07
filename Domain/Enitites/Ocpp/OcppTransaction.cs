using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// One charging session. <see cref="BaseEntity.Id"/> IS the OCPP transactionId we
/// hand the charger in StartTransaction.conf — an identity column, so it never
/// repeats across our own restarts, which matters because the charger keeps it
/// across reboots and may send the StopTransaction hours later (R6).
/// </summary>
public class OcppTransaction : BaseEntity
{
    public int OcppChargePointId { get; set; }
    public int ConnectorId { get; set; }

    /// <summary>The card / password the driver used. Opaque string — never looked up against users.</summary>
    public string IdTag { get; set; } = null!;

    /// <summary>Cumulative meter reading at start, in Wh. The charger's register, not zero.</summary>
    public long MeterStartWh { get; set; }
    public long? MeterStopWh { get; set; }

    /// <summary>Charger timestamps (R1).</summary>
    public DateTime StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }

    /// <summary>Our arrival times (R7).</summary>
    public DateTime ReceivedStartAt { get; set; }
    public DateTime? ReceivedStopAt { get; set; }

    /// <summary>Local, Remote, EVDisconnected, EmergencyStop, PowerLoss, Reboot, DeAuthorized, … stored as sent.</summary>
    public string? StopReason { get; set; }

    /// <summary>(MeterStopWh − MeterStartWh) / 1000. Null while open, or when the meter went backwards.</summary>
    public decimal? EnergyKwh { get; set; }

    public bool IsOpen { get; set; } = true;

    /// <summary>Open for more than 24 h without a StopTransaction — flagged for manual reconciliation.</summary>
    public bool IsStale { get; set; }

    /// <summary>StopTransaction arrived for an id we never started (lost during an outage) — kept, not discarded.</summary>
    public bool IsOrphan { get; set; }

    /// <summary>Authorize/Start answered Invalid for this idTag; the charger will normally stop right away.</summary>
    public bool WasRejected { get; set; }

    public virtual OcppChargePoint ChargePoint { get; set; } = null!;
    public virtual ICollection<OcppMeterValue> MeterValues { get; set; } = new List<OcppMeterValue>();
}
