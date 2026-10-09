// Minimal OCPP 1.6J charger stand-in for the phase-0/B1 tests.
//
//   dotnet run --project Cable.Ocpp.TestClient -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-001 --idle 25 --run 10
//
//   --url      server base URL; the id is appended like a real charger does
//   --id       charge point id                              (default CBL-TEST-001)
//   --user/--password   HTTP Basic credentials (omit for Security Profile 0)
//   --tag      idTag used for the session                   (default CBLTEST0001)
//   --idle     minutes to stay completely silent after boot (default 25 — proves the host
//              does not drop a quiet socket at its 20-minute idle rule)
//   --run      minutes to keep sending Heartbeat every 60 s afterwards (default 5)
//   --plug     simulate plug-in → Authorize → StartTransaction → 3 MeterValues → StopTransaction
//   --replay   like --plug, but drop the socket mid-session, reconnect, resend the SAME MeterValues
//              with the SAME timestamps, then stop — proves R1/R3/R5/R6 (no duplicates, same kWh)
//   --fault    send a Faulted StatusNotification on connector 1, then recover (--fault-code X --fault-info "…" to vary it)
//   --charge-seconds N   with --plug: keep connector 1 Charging for N seconds before StopTransaction
//
// Phase 2: the simulator also answers central-system commands while it is running
// (keep it alive with --run N): TriggerMessage, Reset (closes, reconnects, boots again),
// UnlockConnector, ChangeAvailability, GetConfiguration, ChangeConfiguration.
// SendLocalList / GetLocalListVersion / ClearCache keep an in-memory card list and print it.
// RemoteStopTransaction ends the running --plug session (reason Remote); any other id is Rejected.
// Anything else gets CALLERROR NotImplemented, like a real unit without that profile.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var url = Arg("--url") ?? "ws://localhost:5300/ocpp16/";
var id = Arg("--id") ?? "CBL-TEST-001";
var user = Arg("--user");
var password = Arg("--password");
var tag = Arg("--tag") ?? "CBLTEST0001";
var idleMinutes = int.Parse(Arg("--idle") ?? "25");
var runMinutes = int.Parse(Arg("--run") ?? "5");
var plug = args.Contains("--plug");
var replay = args.Contains("--replay");
var fault = args.Contains("--fault");
var chargeSeconds = int.Parse(Arg("--charge-seconds") ?? "0");
var currentTransactionId = 0;        // the open session, for RemoteStopTransaction
(string Tag, int Connector)? remoteStartTag = null;   // set by RemoteStartTransaction; --remote waits for it and runs the session
var remoteWaitSeconds = int.Parse(Arg("--remote") ?? "0");   // wait this long for a RemoteStartTransaction, then run the session with its tag
var remoteStopRequested = false;     // set by the handler; the charge hold loop ends and Stop goes out with reason Remote   // keep the plug in Charging this long before StopTransaction (UI tests)

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var full = url.TrimEnd('/') + "/" + id;

ClientWebSocket ws = await ConnectAsync();
var cts = new CancellationTokenSource();
var receiver = ReceiveLoop(ws, cts.Token);

await Boot();

if (fault)
{
    var faultCode = Arg("--fault-code") ?? "GroundFailure";
    var faultInfo = Arg("--fault-info") ?? "Residual current detected";
    await Call("StatusNotification", new { connectorId = 1, status = "Faulted", errorCode = faultCode, info = faultInfo, vendorErrorCode = "E-0x21", timestamp = DateTime.UtcNow });
    var faultSeconds = int.Parse(Arg("--fault-seconds") ?? "3");
    Console.WriteLine($"[{Now()}] connector 1 is Faulted ({faultCode}) for {faultSeconds} s");
    await Task.Delay(TimeSpan.FromSeconds(faultSeconds));
    await Call("StatusNotification", new { connectorId = 1, status = "Available", errorCode = "NoError", timestamp = DateTime.UtcNow });
}

if (plug || replay)
{
    // A fixed session clock so a replay resends byte-identical timestamps.
    var t0 = DateTime.UtcNow;
    const long meterStart = 1_000_000;

    await Call("StatusNotification", new { connectorId = 1, status = "Preparing", errorCode = "NoError", timestamp = t0 });
    await Call("Authorize", new { idTag = tag });
    var startReply = await CallAndWait("StartTransaction", new { connectorId = 1, idTag = tag, meterStart, timestamp = t0 });
    var transactionId = startReply.TryGetProperty("transactionId", out var tx) ? tx.GetInt32() : 0;
    currentTransactionId = transactionId;
    Console.WriteLine($"[{Now()}] server assigned transactionId = {transactionId}");
    await Call("StatusNotification", new { connectorId = 1, status = "Charging", errorCode = "NoError", timestamp = t0.AddSeconds(2) });

    object Sample(int i) => new
    {
        connectorId = 1,
        transactionId,
        meterValue = new[]
        {
            new
            {
                timestamp = t0.AddSeconds(15 * i),
                sampledValue = new object[]
                {
                    new { value = (meterStart + i * 500).ToString(), context = "Sample.Periodic", measurand = "Energy.Active.Import.Register", unit = "Wh" },
                    new { value = "30000", measurand = "Power.Active.Import", unit = "W" },
                    new { value = "78.5", measurand = "Current.Import", unit = "A" },
                    new { value = "392.5", measurand = "Voltage", unit = "V" },
                    new { value = (40 + i).ToString(), measurand = "SoC", unit = "Percent" },
                },
            },
        },
    };

    for (var i = 1; i <= 3; i++)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        await Call("MeterValues", Sample(i));
    }

    if (chargeSeconds > 0)
    {
        Console.WriteLine("[" + Now() + "] holding connector 1 in Charging for " + chargeSeconds + " s (--charge-seconds)");
        var holdUntil = DateTime.UtcNow.AddSeconds(chargeSeconds);
        while (DateTime.UtcNow < holdUntil && ws.State == WebSocketState.Open && !remoteStopRequested)
            await Task.Delay(TimeSpan.FromSeconds(2));
        if (remoteStopRequested)
            Console.WriteLine("[" + Now() + "] remote stop requested by the server — ending the session");
    }

    if (replay)
    {
        Console.WriteLine($"[{Now()}] --- simulating network loss: aborting socket, reconnecting in 5 s ---");
        ws.Abort();
        cts.Cancel();
        try { await receiver; } catch (OperationCanceledException) { }
        await Task.Delay(TimeSpan.FromSeconds(5));

        ws = await ConnectAsync();
        cts = new CancellationTokenSource();
        receiver = ReceiveLoop(ws, cts.Token);
        await Boot();   // real chargers boot again after reconnect — must NOT reset the open session (R2)

        Console.WriteLine($"[{Now()}] --- resending the queued samples 1..3 (identical timestamps) + new samples 4..5 ---");
        for (var i = 1; i <= 5; i++)
            await Call("MeterValues", Sample(i));
    }

    var last = replay ? 5 : 3;
    await Call("StatusNotification", new { connectorId = 1, status = "Finishing", errorCode = "NoError", timestamp = DateTime.UtcNow });
    await Call("StopTransaction", new
    {
        transactionId,
        idTag = tag,
        meterStop = meterStart + last * 500,
        timestamp = t0.AddSeconds(15 * last + 5),
        reason = remoteStopRequested ? "Remote" : "Local",
        transactionData = new[]
        {
            new
            {
                timestamp = t0.AddSeconds(15 * last + 5),
                sampledValue = new object[] { new { value = (meterStart + last * 500).ToString(), context = "Transaction.End", measurand = "Energy.Active.Import.Register", unit = "Wh" } },
            },
        },
    });
    await Call("StatusNotification", new { connectorId = 1, status = "Available", errorCode = "NoError", timestamp = t0.AddSeconds(15 * last + 6) });
    Console.WriteLine($"[{Now()}] expected EnergyKwh = {(last * 500) / 1000m:0.000}, meter rows = {last}{(replay ? " (+1 Transaction.End)" : " (+1 Transaction.End)")}");
    currentTransactionId = 0;
    remoteStopRequested = false;
}

if (remoteWaitSeconds > 0)
{
    Console.WriteLine($"[{Now()}] waiting up to {remoteWaitSeconds} s for a RemoteStartTransaction (--remote)");
    var waitUntil = DateTime.UtcNow.AddSeconds(remoteWaitSeconds);
    while (DateTime.UtcNow < waitUntil && remoteStartTag is null && ws.State == WebSocketState.Open)
        await Task.Delay(500);
    if (remoteStartTag is { } rs)
    {
        var t0 = DateTime.UtcNow;
        const long meterStart = 2_000_000;
        await Call("StatusNotification", new { connectorId = rs.Connector, status = "Preparing", errorCode = "NoError", timestamp = t0 });
        await Call("Authorize", new { idTag = rs.Tag });
        var startReply = await CallAndWait("StartTransaction", new { connectorId = rs.Connector, idTag = rs.Tag, meterStart, timestamp = t0 });
        currentTransactionId = startReply.TryGetProperty("transactionId", out var tx) ? tx.GetInt32() : 0;
        var accepted = startReply.TryGetProperty("idTagInfo", out var info) && info.TryGetProperty("status", out var st) && st.GetString() == "Accepted";
        Console.WriteLine($"[{Now()}] remote session transactionId = {currentTransactionId}, idTag {(accepted ? "Accepted" : "REJECTED")}");
        await Call("StatusNotification", new { connectorId = rs.Connector, status = "Charging", errorCode = "NoError", timestamp = t0.AddSeconds(1) });
        var i = 0;
        var holdUntil = DateTime.UtcNow.AddSeconds(chargeSeconds > 0 ? chargeSeconds : 30);
        while (DateTime.UtcNow < holdUntil && ws.State == WebSocketState.Open && !remoteStopRequested)
        {
            i++;
            await Call("MeterValues", new
            {
                connectorId = rs.Connector, transactionId = currentTransactionId,
                meterValue = new[] { new { timestamp = DateTime.UtcNow, sampledValue = new object[]
                {
                    new { value = (meterStart + i * 500).ToString(), context = "Sample.Periodic", measurand = "Energy.Active.Import.Register", unit = "Wh" },
                    new { value = "30000", measurand = "Power.Active.Import", unit = "W" },
                    new { value = (40 + i).ToString(), measurand = "SoC", unit = "Percent" },
                } } },
            });
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        await Call("StatusNotification", new { connectorId = rs.Connector, status = "Finishing", errorCode = "NoError", timestamp = DateTime.UtcNow });
        await Call("StopTransaction", new { transactionId = currentTransactionId, idTag = rs.Tag, meterStop = meterStart + i * 500, timestamp = DateTime.UtcNow, reason = remoteStopRequested ? "Remote" : "Local" });
        await Call("StatusNotification", new { connectorId = rs.Connector, status = "Available", errorCode = "NoError", timestamp = DateTime.UtcNow });
        Console.WriteLine($"[{Now()}] remote session ended, energy {(i * 500) / 1000m:0.000} kWh");
        currentTransactionId = 0; remoteStopRequested = false; remoteStartTag = null;
    }
    else Console.WriteLine($"[{Now()}] no RemoteStartTransaction arrived");
}

if (idleMinutes > 0)
{
    Console.WriteLine($"[{Now()}] going silent for {idleMinutes} min (no frames at all; only transport pings)");
    var until = DateTime.UtcNow.AddMinutes(idleMinutes);
    while (DateTime.UtcNow < until && ws.State == WebSocketState.Open)
        await Task.Delay(TimeSpan.FromSeconds(15));
    Console.WriteLine($"[{Now()}] idle window over, socket state = {ws.State}");
}

var end = DateTime.UtcNow.AddMinutes(runMinutes);
while (DateTime.UtcNow < end && ws.State == WebSocketState.Open)
{
    await Call("Heartbeat", new { });
    await Task.Delay(TimeSpan.FromSeconds(60));
}

Console.WriteLine($"[{Now()}] done, final socket state = {ws.State}");
if (ws.State == WebSocketState.Open)
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
cts.Cancel();
try { await receiver; } catch (OperationCanceledException) { }

async Task<ClientWebSocket> ConnectAsync()
{
    var socket = new ClientWebSocket();
    socket.Options.AddSubProtocol("ocpp1.6");
    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    if (user is not null && password is not null)
        socket.Options.SetRequestHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    Console.WriteLine($"[{Now()}] connecting {full}");
    await socket.ConnectAsync(new Uri(full), CancellationToken.None);
    Console.WriteLine($"[{Now()}] connected, subprotocol = {socket.SubProtocol ?? "(none)"}");
    return socket;
}

async Task Boot()
{
    await Call("BootNotification", new
    {
        chargePointVendor = "Cable",
        chargePointModel = "TestClient",
        chargePointSerialNumber = id,
        firmwareVersion = "0.2",
    });
    // A real unit keeps its availability across a reboot, so an Inoperative plug boots as Unavailable.
    foreach (var k in availability.Keys.Order())
        await Call("StatusNotification", new { connectorId = k, status = availability[k] ? "Available" : "Unavailable", errorCode = "NoError", timestamp = DateTime.UtcNow });
}

async Task Call(string action, object payload)
{
    if (ws.State != WebSocketState.Open)
    {
        Console.WriteLine($"[{Now()}] cannot send {action}: socket is {ws.State}");
        return;
    }
    var frame = JsonSerializer.Serialize(new object[] { 2, Guid.NewGuid().ToString("N")[..12], action, payload }, json);
    await ws.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);
    Console.WriteLine($"[{Now()}] >> {frame}");
}

// Same as Call, but waits for the matching CALLRESULT and returns its payload (needed for transactionId).
async Task<JsonElement> CallAndWait(string action, object payload)
{
    var uid = Guid.NewGuid().ToString("N")[..12];
    var tcs = new TaskCompletionSource<JsonElement>();
    pending[uid] = tcs;
    var frame = JsonSerializer.Serialize(new object[] { 2, uid, action, payload }, json);
    await ws.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);
    Console.WriteLine($"[{Now()}] >> {frame}");
    return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
}

async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
{
    var buffer = new byte[64 * 1024];
    while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
    {
        var result = await socket.ReceiveAsync(buffer, token);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            Console.WriteLine($"[{Now()}] server closed: {result.CloseStatus} {result.CloseStatusDescription}");
            return;
        }
        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
        Console.WriteLine($"[{Now()}] << {text}");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.GetArrayLength() >= 3 && root[0].GetInt32() == 3 && pending.TryRemove(root[1].GetString() ?? "", out var tcs))
                tcs.TrySetResult(root[2].Clone());
            else if (root.GetArrayLength() >= 4 && root[0].GetInt32() == 2)
                await OnServerCall(root[1].GetString() ?? "", root[2].GetString() ?? "", root[3].Clone());
        }
        catch (JsonException) { }
    }
}

// ---- phase 2: central-system commands -------------------------------------------------

async Task OnServerCall(string uid, string action, JsonElement payload)
{
    switch (action)
    {
        case "TriggerMessage":
        {
            var requested = payload.GetProperty("requestedMessage").GetString();
            int? connector = payload.TryGetProperty("connectorId", out var c) ? c.GetInt32() : null;
            await Reply(uid, new { status = "Accepted" });
            switch (requested)
            {
                case "StatusNotification":
                    foreach (var k in connector is int one ? new[] { one } : availability.Keys.Order().ToArray())
                        await Call("StatusNotification", new { connectorId = k, status = availability.GetValueOrDefault(k, true) ? "Available" : "Unavailable", errorCode = "NoError", timestamp = DateTime.UtcNow });
                    break;
                case "Heartbeat": await Call("Heartbeat", new { }); break;
                case "BootNotification": await Boot(); break;
                case "MeterValues":
                    await Call("MeterValues", new { connectorId = connector ?? 1, meterValue = new[] { new { timestamp = DateTime.UtcNow, sampledValue = new object[] { new { value = "1000000", context = "Trigger", measurand = "Energy.Active.Import.Register", unit = "Wh" } } } } });
                    break;
                default:
                    Console.WriteLine($"[{Now()}] TriggerMessage {requested}: accepted, nothing to send in the simulator");
                    break;
            }
            break;
        }
        case "Reset":
        {
            var type = payload.GetProperty("type").GetString();
            await Reply(uid, new { status = "Accepted" });
            Console.WriteLine($"[{Now()}] --- {type} reset: closing in 1 s, rebooting in 4 s ---");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "reset", CancellationToken.None); } catch { ws.Abort(); }
                cts.Cancel();
                await Task.Delay(3000);
                ws = await ConnectAsync();
                cts = new CancellationTokenSource();
                receiver = ReceiveLoop(ws, cts.Token);
                await Boot();
            });
            break;
        }
        case "UnlockConnector":
        {
            var connector = payload.GetProperty("connectorId").GetInt32();
            await Reply(uid, new { status = availability.ContainsKey(connector) ? "Unlocked" : "UnlockFailed" });
            if (availability.ContainsKey(connector))
                await Call("StatusNotification", new { connectorId = connector, status = "Available", errorCode = "NoError", timestamp = DateTime.UtcNow });
            break;
        }
        case "ChangeAvailability":
        {
            var connector = payload.GetProperty("connectorId").GetInt32();
            var operative = payload.GetProperty("type").GetString() == "Operative";
            await Reply(uid, new { status = "Accepted" });
            foreach (var k in connector == 0 ? availability.Keys.Where(k => k > 0).Order().ToArray() : new[] { connector })
            {
                availability[k] = operative;
                await Call("StatusNotification", new { connectorId = k, status = operative ? "Available" : "Unavailable", errorCode = "NoError", timestamp = DateTime.UtcNow });
            }
            break;
        }
        case "GetConfiguration":
        {
            var wanted = payload.TryGetProperty("key", out var keys) && keys.ValueKind == JsonValueKind.Array
                ? keys.EnumerateArray().Select(k => k.GetString() ?? "").ToArray()
                : configuration.Keys.Order().ToArray();
            var known = wanted.Where(configuration.ContainsKey).Select(k => new { key = k, @readonly = configuration[k].ReadOnly, value = configuration[k].Value }).ToArray();
            var unknown = wanted.Where(k => !configuration.ContainsKey(k)).ToArray();
            await Reply(uid, new { configurationKey = known, unknownKey = unknown });
            break;
        }
        case "ChangeConfiguration":
        {
            var key = payload.GetProperty("key").GetString() ?? "";
            var value = payload.GetProperty("value").GetString() ?? "";
            string status;
            if (!configuration.TryGetValue(key, out var entry)) status = "NotSupported";
            else if (entry.ReadOnly) status = "Rejected";
            else { configuration[key] = entry with { Value = value }; status = "Accepted"; }
            await Reply(uid, new { status });
            break;
        }
        case "RemoteStartTransaction":
        {
            // A real unit authorizes the given idTag, then starts on that plug. Here: Accepted when no
            // session is open; the main loop (--remote) runs the session with that tag.
            var ok = currentTransactionId == 0 && !remoteStartTag.HasValue;
            var startTag = payload.GetProperty("idTag").GetString() ?? "";
            var startConnector = payload.TryGetProperty("connectorId", out var rc) ? rc.GetInt32() : 1;
            await Reply(uid, new { status = ok ? "Accepted" : "Rejected" });
            if (ok) { remoteStartTag = (startTag, startConnector); Console.WriteLine($"[{Now()}] remote start accepted: tag {startTag} on connector {startConnector}"); }
            else Console.WriteLine($"[{Now()}] remote start rejected — a session is already open");
            break;
        }
        case "RemoteStopTransaction":
        {
            var requested = payload.GetProperty("transactionId").GetInt32();
            var ok = currentTransactionId != 0 && requested == currentTransactionId;
            await Reply(uid, new { status = ok ? "Accepted" : "Rejected" });
            if (ok) remoteStopRequested = true;
            else Console.WriteLine($"[{Now()}] RemoteStop for {requested} rejected — open session is {currentTransactionId}");
            break;
        }
        case "SendLocalList":
        {
            var version = payload.GetProperty("listVersion").GetInt32();
            var updateType = payload.GetProperty("updateType").GetString();
            var entries = payload.TryGetProperty("localAuthorizationList", out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().ToList() : new();
            if (updateType == "Full") localList.Clear();
            foreach (var e in entries)
            {
                var idTag = e.GetProperty("idTag").GetString() ?? "";
                if (e.TryGetProperty("idTagInfo", out var info))
                    localList[idTag] = info.TryGetProperty("expiryDate", out var exp) ? exp.GetDateTime() : null;
                else
                    localList.TryRemove(idTag, out _);   // differential entry without idTagInfo = delete
            }
            localListVersion = version;
            await Reply(uid, new { status = "Accepted" });
            Console.WriteLine($"[{Now()}] local list v{version} ({updateType}): {localList.Count} card(s) — {string.Join(", ", localList.Keys)}");
            break;
        }
        case "GetLocalListVersion":
            await Reply(uid, new { listVersion = localListVersion });
            break;
        case "ClearCache":
            await Reply(uid, new { status = "Accepted" });
            Console.WriteLine($"[{Now()}] authorization cache cleared");
            break;
        default:
            var err = JsonSerializer.Serialize(new object[] { 4, uid, "NotImplemented", $"{action} is not supported by the simulator", new { } }, json);
            await ws.SendAsync(Encoding.UTF8.GetBytes(err), WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"[{Now()}] >> {err}");
            break;
    }
}

async Task Reply(string uid, object payload)
{
    var frame = JsonSerializer.Serialize(new object[] { 3, uid, payload }, json);
    await ws.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);
    Console.WriteLine($"[{Now()}] >> {frame}");
}

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string Now() => DateTime.UtcNow.ToString("HH:mm:ss");

partial class Program
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();

    /// <summary>connectorId → operative. 0 is the unit itself.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> availability =
        new(new[] { KeyValuePair.Create(0, true), KeyValuePair.Create(1, true), KeyValuePair.Create(2, true) });

    record ConfigEntry(string Value, bool ReadOnly);

    /// <summary>idTag → expiry, as pushed by SendLocalList. What a real unit consults when the server is unreachable.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime?> localList = new();
    static int localListVersion = 0;   // 0 = empty list (OCPP 1.6), -1 would mean unsupported

    /// <summary>A plausible 1.6 key set — what RH4's GetConfiguration will look like.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConfigEntry> configuration = new(new Dictionary<string, ConfigEntry>
    {
        ["SupportedFeatureProfiles"] = new("Core,FirmwareManagement,LocalAuthListManagement,Reservation,RemoteTrigger", true),
        ["NumberOfConnectors"] = new("2", true),
        ["HeartbeatInterval"] = new("60", false),
        ["MeterValueSampleInterval"] = new("15", false),
        ["MeterValuesSampledData"] = new("Energy.Active.Import.Register,Power.Active.Import,Current.Import,Voltage,SoC", false),
        ["ClockAlignedDataInterval"] = new("0", false),
        ["ConnectionTimeOut"] = new("60", false),
        ["AuthorizeRemoteTxRequests"] = new("true", false),
        ["LocalAuthorizeOffline"] = new("true", false),
        ["LocalPreAuthorize"] = new("false", false),
        ["AllowOfflineTxForUnknownId"] = new("false", false),
        ["LocalAuthListEnabled"] = new("true", false),
        ["LocalAuthListMaxLength"] = new("100", true),
        ["SendLocalListMaxLength"] = new("100", true),
        ["StopTransactionOnInvalidId"] = new("true", false),
        ["UnlockConnectorOnEVSideDisconnect"] = new("true", false),
        ["WebSocketPingInterval"] = new("30", false),
        ["CentralSystemUrl"] = new("wss://ocpp.cable-app.com/ocpp16/", false),   // writable on the unit — Cable refuses it (protected key)
        ["AuthorizationKey"] = new("", false),
    });
}
