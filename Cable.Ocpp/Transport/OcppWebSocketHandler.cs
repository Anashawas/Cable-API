using System.Net.WebSockets;
using System.Text;
using Cable.Ocpp.Persistence;
using Cable.Ocpp.Protocol;
using Microsoft.Extensions.Options;

namespace Cable.Ocpp.Transport;

/// <summary>Owns one charger socket from handshake to close.</summary>
public sealed class OcppWebSocketHandler(
    ConnectionRegistry registry,
    MessageDispatcher dispatcher,
    RawMessageStore store,
    IServiceScopeFactory scopes,
    IOptions<OcppOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<OcppWebSocketHandler> log)
{
    private const string Subprotocol = "ocpp1.6";

    public async Task HandleAsync(HttpContext context, string chargePointId)
    {
        var opts = options.Value;
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket handshake expected (OCPP 1.6J endpoint).");
            return;
        }

        // 1. Who is this? Registered, enabled, not locked, credentials valid (when required).
        ChargerAuthResult auth;
        using (var scope = scopes.CreateScope())
        {
            auth = await scope.ServiceProvider.GetRequiredService<ChargerAuthenticator>()
                .AuthenticateAsync(chargePointId, context.Request.Headers.Authorization, context.RequestAborted);
        }

        if (!auth.Ok)
        {
            log.LogWarning("{ChargePointId} from {Ip} refused: {Status} {Reason}", chargePointId, remoteIp, auth.StatusCode, auth.Reason);
            store.Log(new RawMessage(chargePointId, "sys", null, null, "REFUSED",
                $"{{\"status\":{auth.StatusCode},\"reason\":\"{auth.Reason}\"}}", remoteIp, DateTime.UtcNow));
            context.Response.StatusCode = auth.StatusCode;
            if (auth.StatusCode == StatusCodes.Status401Unauthorized)
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"ocpp\"";
            await context.Response.WriteAsync(auth.Reason);
            return;
        }

        // 2. Subprotocol: required by the spec, logged-only for the pilot (RequireSubprotocol).
        var offered = context.WebSockets.WebSocketRequestedProtocols;
        var selected = offered.Any(p => string.Equals(p, Subprotocol, StringComparison.OrdinalIgnoreCase)) ? Subprotocol : null;
        if (selected is null)
        {
            log.LogWarning("{ChargePointId} from {Ip} offered subprotocols [{Offered}] — expected {Expected}",
                chargePointId, remoteIp, string.Join(", ", offered), Subprotocol);
            if (opts.RequireSubprotocol)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Sec-WebSocket-Protocol must include ocpp1.6.");
                return;
            }
        }

        // 3. Accept and register. A reconnect replaces the stale socket for the same id.
        using var socket = selected is null
            ? await context.WebSockets.AcceptWebSocketAsync()
            : await context.WebSockets.AcceptWebSocketAsync(selected);

        var cp = auth.ChargePoint!;
        var session = new OcppSession(cp.ChargePointId, cp.Id, cp.ChargingPointId, cp.HeartbeatInterval, socket, remoteIp, selected);
        var replaced = await registry.RegisterAsync(session, context.RequestAborted);
        store.Log(new RawMessage(chargePointId, "sys", null, null, "CONNECT",
            $"{{\"subprotocol\":\"{selected}\",\"offered\":\"{string.Join(",", offered)}\",\"replacedPrevious\":{(replaced is not null).ToString().ToLowerInvariant()}}}",
            remoteIp, DateTime.UtcNow));
        await WithStateAsync(s => s.MarkConnectedAsync(cp.Id, remoteIp, CancellationToken.None), chargePointId, "mark connected");

        string closeReason = "client closed";
        try
        {
            await PumpAsync(session, opts, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Kestrel aborts the request both when we stop and when the peer drops the TCP connection
            // (a charger rebooting, a cut cable). Only the first is our fault and excluded from reliability.
            closeReason = lifetime.ApplicationStopping.IsCancellationRequested ? "server shutting down" : "connection lost";
        }
        catch (WebSocketException ex)
        {
            closeReason = $"socket error: {ex.WebSocketErrorCode}";
            log.LogWarning(ex, "{ChargePointId} socket error", chargePointId);
        }
        catch (Exception ex)
        {
            closeReason = "unhandled error";
            log.LogError(ex, "{ChargePointId} unhandled error in pump", chargePointId);
        }
        finally
        {
            session.Pending.FailAll($"socket closed ({closeReason})");
            var stillCurrent = registry.Unregister(session);
            store.Log(new RawMessage(chargePointId, "sys", null, null, "DISCONNECT",
                $"{{\"reason\":\"{closeReason}\",\"messagesIn\":{session.MessagesIn},\"messagesOut\":{session.MessagesOut},\"stillCurrent\":{stillCurrent.ToString().ToLowerInvariant()}}}",
                remoteIp, DateTime.UtcNow));

            // Only the current socket may flip the row to disconnected — the replacement is already live.
            if (stillCurrent)
                await WithStateAsync(s => s.MarkDisconnectedAsync(cp.Id, CancellationToken.None), chargePointId, "mark disconnected");

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* peer already gone */ }
            }
        }
    }

    private async Task WithStateAsync(Func<ConnectionStateService, Task> action, string chargePointId, string what)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<ConnectionStateService>());
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{ChargePointId}: could not {What}", chargePointId, what);
        }
    }

    private async Task PumpAsync(OcppSession session, OcppOptions opts, CancellationToken cancellationToken)
    {
        var socket = session.Socket;
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                message.Write(buffer, 0, result.Count);
                if (message.Length > opts.MaxMessageBytes)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame exceeds limit", cancellationToken);
                    return;
                }
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue; // OCPP-J is text only; ignore binary frames.

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            await OnFrameAsync(session, text, cancellationToken);
        }
    }

    private async Task OnFrameAsync(OcppSession session, string text, CancellationToken cancellationToken)
    {
        var parsed = OcppFrame.TryParse(text, out var frame, out var errorCode, out var errorDescription);
        session.TouchIn(frame?.Action);

        // R4: a reply to one of our CALLs is matched by uniqueId, never by order. Done before
        // logging so the raw row carries the command's action (a CALLRESULT has none on the wire).
        string? answeredAction = null;
        if (frame is { MessageType: OcppMessageType.CallResult or OcppMessageType.CallError })
        {
            answeredAction = session.Pending.TryComplete(frame);
            if (answeredAction is null)
                log.LogInformation("{ChargePointId} sent an unsolicited {Type} for id {UniqueId}", session.ChargePointId, frame.MessageType, frame.UniqueId);
        }

        store.Log(new RawMessage(session.ChargePointId, "in", frame?.MessageType, frame?.UniqueId,
            frame?.Action ?? answeredAction ?? (parsed ? null : "MALFORMED"), text, session.RemoteIp, DateTime.UtcNow));

        if (!parsed)
        {
            var reply = OcppFrame.CallError("", errorCode!, errorDescription!);
            await SendAsync(session, OcppMessageType.CallError, "", "MALFORMED", reply, cancellationToken);
            return;
        }

        switch (frame!.MessageType)
        {
            case OcppMessageType.Call:
            {
                var reply = await dispatcher.HandleAsync(session, frame, cancellationToken);
                var type = reply.StartsWith("[4", StringComparison.Ordinal) ? OcppMessageType.CallError : OcppMessageType.CallResult;
                await SendAsync(session, type, frame.UniqueId, frame.Action, reply, cancellationToken);
                break;
            }

            case OcppMessageType.CallResult:
            case OcppMessageType.CallError:
                break; // already matched above
        }
    }

    private async Task SendAsync(OcppSession session, int messageType, string uniqueId, string? action, string text, CancellationToken cancellationToken)
    {
        await session.SendTextAsync(text, cancellationToken);
        store.Log(new RawMessage(session.ChargePointId, "out", messageType, uniqueId, action, text, session.RemoteIp, DateTime.UtcNow));
    }
}
