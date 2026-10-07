using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Last known state of one connector, upserted from StatusNotification.
/// ConnectorId 0 is the charger itself; 1..n are the plugs. Unknown connector ids
/// are created on first sight rather than rejected.
/// </summary>
public class OcppConnector : BaseEntity
{
    public int OcppChargePointId { get; set; }
    public int ConnectorId { get; set; }

    /// <summary>One of OcppConnectorStatus (Available, Preparing, Charging, … Faulted).</summary>
    public string Status { get; set; } = "Unknown";
    public string ErrorCode { get; set; } = "NoError";
    public string? VendorErrorCode { get; set; }
    public string? Info { get; set; }

    /// <summary>The charger's own timestamp from the message (R1) — what the app orders by.</summary>
    public DateTime? StatusUpdatedAt { get; set; }

    /// <summary>When we received it (R7); the gap to StatusUpdatedAt is the charger's clock drift or queue delay.</summary>
    public DateTime StatusReceivedAt { get; set; }

    /// <summary>Optional link to the station's declared plug types for display.</summary>
    public int? PlugTypeId { get; set; }
    public decimal? PowerKw { get; set; }

    public virtual OcppChargePoint ChargePoint { get; set; } = null!;
    public virtual PlugType? PlugType { get; set; }
}
