using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// "The plug didn't work" (§13.3): a driver tells the station that a start failed. Carries what we knew at that
/// moment — the plug's last status and, when the app sends the start's commandId, what the charger answered — so
/// the owner sees the problem without asking. Three open reports within an hour mark the plug "may have a problem"
/// in /live until someone resolves them or a session succeeds on it.
/// </summary>
public class OcppConnectorReport : BaseAuditableEntity
{
    public int ChargingPointId { get; set; }
    public int OcppChargePointId { get; set; }
    public int ConnectorId { get; set; }

    /// <summary>The driver who reported it.</summary>
    public int UserId { get; set; }

    /// <summary>NoResponse | Rejected | ScreenError | Damaged | Other (OcppConnectorReportReason).</summary>
    public string Reason { get; set; } = null!;
    public string? Note { get; set; }

    /// <summary>The failed RemoteStartTransaction, when the app sent its id; with the transport / charger outcome copied.</summary>
    public int? OcppCommandId { get; set; }
    public string? CommandStatus { get; set; }
    public string? CommandResultStatus { get; set; }
    public string? CommandErrorCode { get; set; }

    /// <summary>The plug's last known state when the report came in.</summary>
    public string? ConnectorStatus { get; set; }
    public string? ConnectorErrorCode { get; set; }
    public DateTime? ConnectorStatusAt { get; set; }
    public bool ChargerOnline { get; set; }

    /// <summary>Null = open. Set when the owner / a manager / an admin marks the plug fixed.</summary>
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }

    public virtual OcppChargePoint ChargePoint { get; set; } = null!;
}
