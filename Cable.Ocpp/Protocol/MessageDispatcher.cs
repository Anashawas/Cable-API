using Cable.Ocpp.Handlers;
using Cable.Ocpp.Transport;
using Microsoft.Extensions.Options;

namespace Cable.Ocpp.Protocol;

/// <summary>
/// Routes one CALL to its keyed <see cref="IOcppHandler"/> inside a fresh DI scope,
/// so every message gets its own DbContext and nothing leaks between chargers.
/// Also persists LastMessageAt, throttled, so the API can show "last seen".
/// </summary>
public sealed class MessageDispatcher(IServiceScopeFactory scopes, IOptions<OcppOptions> options, ILogger<MessageDispatcher> log)
{
    public async Task<string> HandleAsync(OcppSession session, OcppInbound call, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var handler = scope.ServiceProvider.GetKeyedService<IOcppHandler>(call.Action);
        if (handler is null)
        {
            // Never close the socket over an unknown action — answer and keep listening.
            return OcppFrame.CallError(call.UniqueId, OcppErrorCode.NotImplemented, $"Action '{call.Action}' is not supported by this server");
        }

        string reply;
        try
        {
            var payload = await handler.HandleAsync(session, call.Payload, cancellationToken);
            reply = OcppFrame.CallResult(call.UniqueId, payload);
        }
        catch (OcppCallErrorException ex)
        {
            reply = OcppFrame.CallError(call.UniqueId, ex.ErrorCode, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "{ChargePointId}: handler for {Action} failed", session.ChargePointId, call.Action);
            reply = OcppFrame.CallError(call.UniqueId, OcppErrorCode.InternalError, "Internal error while processing the request");
        }

        await TouchIfDueAsync(scope, session, cancellationToken);
        return reply;
    }

    private async Task TouchIfDueAsync(IServiceScope scope, OcppSession session, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if ((now - session.LastPersistedTouchAt).TotalSeconds < options.Value.TouchIntervalSeconds)
            return;

        try
        {
            var stillAllowed = await scope.ServiceProvider.GetRequiredService<ConnectionStateService>()
                .TouchAsync(session.OcppChargePointId, cancellationToken);
            session.LastPersistedTouchAt = now;

            if (!stillAllowed && session.Socket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                // Disabled or deleted by an admin while connected: the next handshake will be refused,
                // so close now rather than letting it report for up to its next reboot.
                log.LogWarning("{ChargePointId}: disabled by admin — closing socket", session.ChargePointId);
                await session.Socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
                    "charge point disabled", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{ChargePointId}: could not persist LastMessageAt", session.ChargePointId);
        }
    }
}
