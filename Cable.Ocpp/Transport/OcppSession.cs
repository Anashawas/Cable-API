using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Cable.Ocpp.Transport;

/// <summary>One live charger socket plus what we have learned about it since it connected.</summary>
public sealed class OcppSession(
    string chargePointId,
    int ocppChargePointId,
    int chargingPointId,
    int heartbeatInterval,
    WebSocket socket,
    string? remoteIp,
    string? subprotocol)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string ChargePointId { get; } = chargePointId;

    /// <summary>OcppChargePoint.Id — what every row this session writes points at.</summary>
    public int OcppChargePointId { get; } = ocppChargePointId;

    /// <summary>The station (tenant). Authorized tags are scoped to it.</summary>
    public int ChargingPointId { get; } = chargingPointId;

    public int HeartbeatInterval { get; set; } = heartbeatInterval;
    public WebSocket Socket { get; } = socket;
    public string? RemoteIp { get; } = remoteIp;
    public string? Subprotocol { get; } = subprotocol;
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; private set; } = DateTime.UtcNow;

    /// <summary>When LastMessageAt was last written to the database (throttled).</summary>
    public DateTime LastPersistedTouchAt { get; set; } = DateTime.UtcNow;

    public long MessagesIn { get; private set; }
    public long MessagesOut { get; private set; }
    public string? LastAction { get; private set; }

    // Filled from BootNotification — the first thing a real unit tells us about itself.
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public DateTime? LastBootAt { get; set; }

    /// <summary>connectorId → last StatusNotification. 0 is the charger itself.</summary>
    public ConcurrentDictionary<int, ConnectorState> Connectors { get; } = new();

    /// <summary>Server-initiated calls awaiting their reply (phase 2 control messages).</summary>
    public Protocol.PendingCalls Pending { get; } = new();

    public void TouchIn(string? action)
    {
        LastMessageAt = DateTime.UtcNow;
        MessagesIn++;
        if (action is not null) LastAction = action;
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
                MessagesOut++;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}

public sealed record ConnectorState(string Status, string ErrorCode, string? Info, DateTime? ChargerTimestamp, DateTime ReceivedAt);
