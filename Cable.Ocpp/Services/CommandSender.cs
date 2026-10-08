using System.Diagnostics;
using System.Text.Json;
using Cable.Ocpp.Persistence;
using Cable.Ocpp.Protocol;
using Cable.Ocpp.Transport;

namespace Cable.Ocpp.Services;

/// <summary>
/// Sends one central-system-initiated CALL to a connected charger and waits for the
/// reply. The API never talks to a socket itself: it POSTs {action, payload} to
/// /commands/{chargePointId} and gets back what the unit answered.
/// </summary>
public sealed class CommandSender(ConnectionRegistry registry, RawMessageStore store, ILogger<CommandSender> log)
{
    /// <summary>
    /// OCPP 1.6 operations a central system may initiate. Anything else is refused
    /// here so a bug in the API can never push an arbitrary frame to a live unit.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        // Core
        "ChangeAvailability", "ChangeConfiguration", "ClearCache", "DataTransfer", "GetConfiguration",
        "RemoteStartTransaction", "RemoteStopTransaction", "Reset", "UnlockConnector",
        // Firmware management
        "GetDiagnostics", "UpdateFirmware",
        // Local auth list
        "GetLocalListVersion", "SendLocalList",
        // Reservation
        "CancelReservation", "ReserveNow",
        // Smart charging
        "ClearChargingProfile", "GetCompositeSchedule", "SetChargingProfile",
        // Remote trigger
        "TriggerMessage",
    };

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<CommandOutcome> SendAsync(string chargePointId, string action, JsonElement payload, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (!AllowedActions.Contains(action))
            return CommandOutcome.Failed(OcppCommandStatus.Invalid, $"'{action}' is not a central-system-initiated OCPP 1.6 action");
        if (payload.ValueKind != JsonValueKind.Object)
            return CommandOutcome.Failed(OcppCommandStatus.Invalid, "payload must be a JSON object");

        // Safety guards, enforced here as well as in the API so nothing holding the internal key can bypass them.
        if (action == "ChangeConfiguration")
        {
            var key = payload.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            if (Cable.Core.Constants.OcppProtectedConfigurationKeys.IsProtected(key))
                return CommandOutcome.Failed(OcppCommandStatus.Invalid, $"configuration key '{key}' is protected (server address / identity) and cannot be changed remotely");
        }

        var session = registry.Find(chargePointId);
        if (session is null)
            return CommandOutcome.Failed(OcppCommandStatus.NotConnected, $"{chargePointId} is not connected to this server");

        if (action == "UnlockConnector"
            && payload.TryGetProperty("connectorId", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var connectorId)
            && session.Connectors.TryGetValue(connectorId, out var state)
            && state.Status == Cable.Core.Constants.OcppConnectorStatus.Charging)
            return CommandOutcome.Failed(OcppCommandStatus.Invalid, $"connector {connectorId} is Charging; unlocking a live cable is refused");

        // Rate limit (second line of defence after the API's): N commands per charger per minute.
        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        while (session.RecentCommands.TryPeek(out var oldest) && oldest < cutoff)
            session.RecentCommands.TryDequeue(out _);
        if (session.RecentCommands.Count >= Cable.Core.Constants.OcppLimits.CommandsPerChargerPerMinute)
            return CommandOutcome.Failed(OcppCommandStatus.Invalid, $"rate limit: {session.RecentCommands.Count} commands to {chargePointId} in the last minute");
        session.RecentCommands.Enqueue(DateTime.UtcNow);

        var uniqueId = Guid.NewGuid().ToString("N")[..16];
        var frame = OcppFrame.Call(uniqueId, action, payload);
        var pending = session.Pending.Register(uniqueId, action);
        var clock = Stopwatch.StartNew();

        try
        {
            await session.SendTextAsync(frame, cancellationToken);
            store.Log(new RawMessage(session.ChargePointId, "out", OcppMessageType.Call, uniqueId, action, frame, session.RemoteIp, DateTime.UtcNow));

            var reply = await pending.Task.WaitAsync(timeout ?? DefaultTimeout, cancellationToken);
            clock.Stop();

            return reply.MessageType == OcppMessageType.CallError
                ? new CommandOutcome(OcppCommandStatus.CallError, null, reply.ErrorCode, reply.ErrorDescription, clock.ElapsedMilliseconds)
                : new CommandOutcome(OcppCommandStatus.Answered, reply.Payload.GetRawText(), null, null, clock.ElapsedMilliseconds);
        }
        catch (TimeoutException)
        {
            log.LogWarning("{ChargePointId}: no reply to {Action} ({UniqueId}) within {Timeout}s", chargePointId, action, uniqueId, (timeout ?? DefaultTimeout).TotalSeconds);
            return CommandOutcome.Failed(OcppCommandStatus.Timeout, $"{chargePointId} did not answer {action} within {(timeout ?? DefaultTimeout).TotalSeconds:0} s", clock.ElapsedMilliseconds);
        }
        catch (OcppCommandException ex)
        {
            return CommandOutcome.Failed(ex.Status, ex.Message, clock.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "{ChargePointId}: sending {Action} failed", chargePointId, action);
            return CommandOutcome.Failed(OcppCommandStatus.Disconnected, $"could not send {action}: {ex.Message}", clock.ElapsedMilliseconds);
        }
        finally
        {
            session.Pending.Forget(uniqueId);
        }
    }
}

/// <summary>What the API gets back. <see cref="Payload"/> is the raw CALLRESULT JSON when <see cref="Status"/> is Answered.</summary>
public sealed record CommandOutcome(string Status, string? Payload, string? ErrorCode, string? ErrorDescription, long ElapsedMs)
{
    public static CommandOutcome Failed(string status, string description, long elapsedMs = 0) =>
        new(status, null, null, description, elapsedMs);
}
