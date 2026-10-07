using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Audit trail of every central-system command sent to a charger (Reset, UnlockConnector,
/// ChangeAvailability, TriggerMessage, …): who asked, what was sent, what the unit
/// answered and how long it took. Written by the API after Cable.Ocpp reports back.
/// </summary>
public class OcppCommand : BaseAuditableEntity
{
    public int OcppChargePointId { get; set; }

    /// <summary>OCPP action name, e.g. "Reset".</summary>
    public string Action { get; set; } = null!;

    /// <summary>The CALL payload as sent (JSON).</summary>
    public string RequestPayload { get; set; } = null!;

    /// <summary>Answered | CallError | NotConnected | Timeout | Disconnected | Invalid | Unreachable.</summary>
    public string Status { get; set; } = null!;

    /// <summary>The unit's own status word from the CALLRESULT (Accepted, Rejected, Unlocked, …), when present.</summary>
    public string? ResultStatus { get; set; }

    /// <summary>The CALLRESULT payload (JSON) when answered.</summary>
    public string? ResponsePayload { get; set; }

    /// <summary>CALLERROR code, or a transport/validation error description.</summary>
    public string? ErrorCode { get; set; }
    public string? ErrorDescription { get; set; }

    public int? DurationMs { get; set; }

    /// <summary>Set once the unit confirms the command had its effect (e.g. StatusNotification after ChangeAvailability).</summary>
    public DateTime? CompletedAt { get; set; }

    public virtual OcppChargePoint ChargePoint { get; set; } = null!;
}
