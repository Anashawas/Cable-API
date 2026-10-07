using Cable.Core.Constants;

namespace Cable.Ocpp;

public sealed class OcppOptions
{
    public const string SectionName = "Ocpp";

    /// <summary>Default heartbeat interval for chargers registered without one; the per-charger row wins.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    /// <summary>Server-side WebSocket ping. Keeps NAT/firewalls from dropping idle sockets.</summary>
    public int PingIntervalSeconds { get; set; } = 30;

    /// <summary>Largest OCPP frame accepted; anything bigger closes the socket.</summary>
    public int MaxMessageBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Reject the handshake when the charger does not offer "ocpp1.6". Off for the
    /// pilot so we can see exactly what a real unit sends.
    /// </summary>
    public bool RequireSubprotocol { get; set; } = false;

    /// <summary>
    /// See <see cref="OcppAuthorizeMode"/>: List (production), AcceptAll (simulator only),
    /// RejectAll (connection test on a live station where nobody should charge through us).
    /// </summary>
    public string AuthorizeMode { get; set; } = OcppAuthorizeMode.List;

    /// <summary>Persist LastMessageAt at most this often per charger — the socket is the live signal, the column is for the API.</summary>
    public int TouchIntervalSeconds { get; set; } = 60;

    /// <summary>Shared secret for GET /status (X-Api-Key header). Empty disables /status.</summary>
    public string StatusApiKey { get; set; } = "";

    /// <summary>
    /// Our own public /health URL, requested every <see cref="SelfPingIntervalSeconds"/> so a
    /// shared host's idle timeout never stops the pool while chargers are connected.
    /// Empty = off (VPS / local).
    /// </summary>
    public string SelfPingUrl { get; set; } = "";

    public int SelfPingIntervalSeconds { get; set; } = 240;
}
