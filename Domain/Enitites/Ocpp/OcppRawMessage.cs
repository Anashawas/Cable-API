namespace Domain.Enitites;

/// <summary>
/// Every OCPP frame in and out, plus connect/disconnect events. Written by
/// Cable.Ocpp on a background channel (plain SqlClient, batched); mapped here so
/// the API can show a charger's recent traffic and the purge job can trim it to
/// 30 days. Treat as read-only from EF.
/// </summary>
public class OcppRawMessage
{
    public long Id { get; set; }
    public string ChargePointId { get; set; } = null!;

    /// <summary>"in" | "out" | "sys"</summary>
    public string Direction { get; set; } = null!;

    /// <summary>2 CALL | 3 CALLRESULT | 4 CALLERROR; null for sys rows.</summary>
    public byte? MessageType { get; set; }
    public string? MessageId { get; set; }
    public string? Action { get; set; }
    public string? Payload { get; set; }
    public string? RemoteIp { get; set; }
    public DateTime CreatedAt { get; set; }
}
