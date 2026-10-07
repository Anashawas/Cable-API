namespace Application.Common.Interfaces;

/// <summary>
/// Sends a central-system command to a charger through the Cable.Ocpp host (the
/// separate process that owns the WebSocket). Implemented over HTTP in Infrastructure.
/// </summary>
public interface IOcppCommandClient
{
    /// <summary>Public base URL of the OCPP host as configured (https://ocpp.cable-app.com); null when not configured.</summary>
    string? ServerUrl { get; }

    /// <param name="chargePointId">The OCPP id (the one in the socket URL), not the row id.</param>
    /// <param name="action">OCPP 1.6 action name, e.g. "Reset".</param>
    /// <param name="payload">Serialised as the CALL payload.</param>
    Task<OcppCommandOutcome> SendAsync(string chargePointId, string action, object payload, CancellationToken cancellationToken, int? timeoutSeconds = null);
}

/// <summary>
/// Mirrors Cable.Ocpp's CommandOutcome. <see cref="Status"/> is the transport outcome
/// (Answered | CallError | NotConnected | Timeout | Disconnected | Invalid | Unreachable);
/// the charger's own verdict (Accepted / Rejected / …) lives inside <see cref="Payload"/>.
/// </summary>
public sealed record OcppCommandOutcome(string Status, string? Payload, string? ErrorCode, string? ErrorDescription, long ElapsedMs)
{
    public bool Answered => Status == "Answered";
}

/// <summary>What the technician types into the charger, derived from the OCPP host URL.</summary>
public static class OcppEndpoint
{
    /// <summary>https://host[:port] → wss://host[:port]/ocpp16/ (the unit appends its own id).</summary>
    public static string? WebSocketBaseUrl(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || !Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var u)) return null;
        var scheme = u.Scheme == "http" ? "ws" : "wss";
        var port = u.IsDefaultPort ? "" : ":" + u.Port;
        return scheme + "://" + u.Host + port + "/ocpp16/";
    }

    /// <summary>443 for wss, 80 for ws, or the explicit port — for units that have a separate port field.</summary>
    public static int? Port(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || !Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var u)) return null;
        return u.IsDefaultPort ? (u.Scheme == "http" ? 80 : 443) : u.Port;
    }
}
