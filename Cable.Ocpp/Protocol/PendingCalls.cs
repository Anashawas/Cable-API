using System.Collections.Concurrent;

namespace Cable.Ocpp.Protocol;

/// <summary>
/// Server-initiated CALLs waiting for their CALLRESULT / CALLERROR (R4: matched by
/// uniqueId, never by order). One instance per session; a socket that closes fails
/// every call still pending on it so no caller waits for the full timeout.
/// </summary>
public sealed class PendingCalls
{
    private readonly ConcurrentDictionary<string, (string Action, TaskCompletionSource<OcppInbound> Tcs)> _pending = new(StringComparer.Ordinal);

    public int Count => _pending.Count;

    public TaskCompletionSource<OcppInbound> Register(string uniqueId, string action)
    {
        var tcs = new TaskCompletionSource<OcppInbound>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[uniqueId] = (action, tcs);
        return tcs;
    }

    public void Forget(string uniqueId) => _pending.TryRemove(uniqueId, out _);

    /// <summary>
    /// Completes the waiting call and returns its action name (a CALLRESULT carries none
    /// on the wire, so the raw log gets it from here). Null = nobody was waiting.
    /// </summary>
    public string? TryComplete(OcppInbound reply)
    {
        if (!_pending.TryRemove(reply.UniqueId, out var entry))
            return null;
        entry.Tcs.TrySetResult(reply);
        return entry.Action;
    }

    public void FailAll(string reason)
    {
        foreach (var key in _pending.Keys.ToArray())
            if (_pending.TryRemove(key, out var entry))
                entry.Tcs.TrySetException(new OcppCommandException(OcppCommandStatus.Disconnected, reason));
    }
}

/// <summary>Outcome classes a caller can act on. The OCPP payload status (Accepted / Rejected / …) is separate.</summary>
public static class OcppCommandStatus
{
    /// <summary>CALLRESULT received; see the payload's own status field.</summary>
    public const string Answered = "Answered";

    /// <summary>CALLERROR received (the charger did not understand or refused the request).</summary>
    public const string CallError = "CallError";

    /// <summary>The charger is not connected to this server right now.</summary>
    public const string NotConnected = "NotConnected";

    /// <summary>No reply within the timeout.</summary>
    public const string Timeout = "Timeout";

    /// <summary>The socket closed while we were waiting.</summary>
    public const string Disconnected = "Disconnected";

    /// <summary>The request was refused before sending (unknown action, bad payload).</summary>
    public const string Invalid = "Invalid";
}

public sealed class OcppCommandException(string status, string message) : Exception(message)
{
    public string Status { get; } = status;
}
