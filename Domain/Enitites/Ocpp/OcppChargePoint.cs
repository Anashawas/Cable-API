using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// One physical charger (an OCPP "charge point") registered under a station.
/// The charger connects to Cable.Ocpp as <c>/ocpp16/{ChargePointId}</c>; vendor,
/// model and firmware are filled in from its first BootNotification, never typed.
/// Tenant boundary for everything OCPP: a charger belongs to exactly one
/// <see cref="ChargingPoint"/> and is only visible to that station's owner/managers.
/// </summary>
public class OcppChargePoint : BaseAuditableEntity
{
    public int ChargingPointId { get; set; }

    /// <summary>The id the charger sends in the URL. Letters, digits and '-' only; unique system-wide.</summary>
    public string ChargePointId { get; set; } = null!;

    /// <summary>Basic-auth password hash. Null = no credentials required (OCPP Security Profile 0, e.g. the pilot unit).</summary>
    public string? PasswordHash { get; set; }

    public string? DisplayName { get; set; }

    // From BootNotification
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? ChargeBoxSerialNumber { get; set; }
    public string? Iccid { get; set; }
    public string? Imsi { get; set; }
    public string? MeterSerialNumber { get; set; }

    /// <summary>Seconds handed back in BootNotification.conf; the charger heartbeats at this rate.</summary>
    public int HeartbeatInterval { get; set; } = 60;

    /// <summary>Off = handshake refused (station off-boarded) without losing history.</summary>
    public bool IsEnabled { get; set; } = true;

    // Live connection state, maintained by Cable.Ocpp. Connector rows are NOT touched on disconnect (R5).
    public bool IsConnected { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public DateTime? LastBootAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastRemoteIp { get; set; }

    // Brute-force protection on the Basic-auth handshake.
    public int FailedAuthCount { get; set; }
    public DateTime? LockedUntil { get; set; }

    // Local authorization list pushed into the unit (SendLocalList) so it keeps authorizing
    // the station's cards while our server is unreachable. Version = Unix seconds of the push.
    public int? LocalListVersion { get; set; }
    public DateTime? LocalListSyncedAt { get; set; }

    /// <summary>See OcppLocalListStatus: Synced | Pending | Failed | NotSupported. Null = never attempted.</summary>
    public string? LocalListStatus { get; set; }

    // N-6 reliability over the last OcppLimits.ReliabilityWindowDays, recomputed daily by
    // ComputeOcppReliabilityAsync from the raw log (CONNECT / DISCONNECT / StatusNotification).
    // Pct = share of minutes the unit was reachable AND had no Faulted plug; our own restarts
    // ("server shutting down" disconnects) are excluded from the denominator.
    public decimal? ReliabilityPct { get; set; }
    public decimal? ReliabilityOnlinePct { get; set; }
    public decimal? ReliabilityFaultFreePct { get; set; }
    public int? ReliabilityOfflineIncidents { get; set; }
    public int? ReliabilityFaultIncidents { get; set; }
    public DateTime? ReliabilityComputedAt { get; set; }

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
    public virtual ICollection<OcppConnector> Connectors { get; set; } = new List<OcppConnector>();
    public virtual ICollection<OcppTransaction> Transactions { get; set; } = new List<OcppTransaction>();
}
