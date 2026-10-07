using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Cable.Ocpp.Transport;

/// <summary>
/// ChargePointId → live session. One socket per charger: a charger that reconnects
/// (after our recycle, its reboot, a network blip) replaces its stale entry and the
/// old socket is closed, so a half-dead connection never shadows the live one.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, OcppSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _sessions.Count;

    public IReadOnlyCollection<OcppSession> All => _sessions.Values.ToArray();

    /// <summary>The live session for a charger, or null when it is not connected to this process.</summary>
    public OcppSession? Find(string chargePointId) =>
        _sessions.TryGetValue(chargePointId, out var session) && session.Socket.State == WebSocketState.Open ? session : null;

    public async Task<OcppSession?> RegisterAsync(OcppSession session, CancellationToken cancellationToken)
    {
        var previous = _sessions.AddOrUpdate(session.ChargePointId, session, (_, _) => session);
        if (ReferenceEquals(previous, session))
            return null;

        try
        {
            if (previous.Socket.State == WebSocketState.Open)
                await previous.Socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Replaced by a new connection", cancellationToken);
        }
        catch
        {
            // The old socket is already gone; nothing to do.
        }

        return previous;
    }

    /// <summary>Removes the session only if it is still the current one for that charger.</summary>
    public bool Unregister(OcppSession session) =>
        _sessions.TryGetValue(session.ChargePointId, out var current)
        && ReferenceEquals(current, session)
        && _sessions.TryRemove(session.ChargePointId, out _);
}
