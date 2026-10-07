using Application.Common.Interfaces;
using Cable.Ocpp;
using Cable.Ocpp.Handlers;
using Cable.Ocpp.Persistence;
using Cable.Ocpp.Protocol;
using Cable.Ocpp.Services;
using Cable.Ocpp.Transport;
using Infrastructrue;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();

builder.Services.Configure<OcppOptions>(builder.Configuration.GetSection(OcppOptions.SectionName));

// Shared with the API: the EF DbContext + audit interceptor, the password hasher,
// and a Hangfire *client* so fault notifications run in the API's job server.
// Not AddApplication(): that registers every API handler and their upload/PDF/
// analytics dependencies. The DbContext only needs an IMediator to dispatch
// domain events, and nothing handles those here — so a bare MediatR is enough.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddCableDatabase(builder.Configuration);
builder.Services.AddCableHangfireClient(builder.Configuration);
builder.Services.AddPasswordHasher();
builder.Services.AddScoped<ICurrentUserService, OcppSystemUserService>();

// Transport
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<OcppWebSocketHandler>();
builder.Services.AddScoped<ChargerAuthenticator>();
builder.Services.AddScoped<ConnectionStateService>();

// Persistence / services
builder.Services.AddSingleton<RawMessageStore>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RawMessageStore>());
builder.Services.AddSingleton<ProcessLifetime>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProcessLifetime>());
builder.Services.AddSingleton<FaultNotifier>();
builder.Services.AddSingleton<CommandSender>();
builder.Services.AddHttpClient(nameof(KeepAliveSelfPing));
builder.Services.AddHostedService<KeepAliveSelfPing>();
builder.Services.AddScoped<TagAuthorizer>();
builder.Services.AddScoped<CommandConfirmer>();

// Charger-initiated actions, keyed by OCPP action name. Anything else → NotImplemented.
builder.Services.AddKeyedScoped<IOcppHandler, BootNotificationHandler>("BootNotification");
builder.Services.AddKeyedScoped<IOcppHandler, HeartbeatHandler>("Heartbeat");
builder.Services.AddKeyedScoped<IOcppHandler, StatusNotificationHandler>("StatusNotification");
builder.Services.AddKeyedScoped<IOcppHandler, AuthorizeHandler>("Authorize");
builder.Services.AddKeyedScoped<IOcppHandler, StartTransactionHandler>("StartTransaction");
builder.Services.AddKeyedScoped<IOcppHandler, MeterValuesHandler>("MeterValues");
builder.Services.AddKeyedScoped<IOcppHandler, StopTransactionHandler>("StopTransaction");
builder.Services.AddKeyedScoped<IOcppHandler, DataTransferHandler>("DataTransfer");
builder.Services.AddKeyedScoped<IOcppHandler, AcknowledgeHandler>("FirmwareStatusNotification");
builder.Services.AddKeyedScoped<IOcppHandler, AcknowledgeHandler>("DiagnosticsStatusNotification");

var app = builder.Build();

var ocpp = app.Services.GetRequiredService<IOptions<OcppOptions>>().Value;

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(ocpp.PingIntervalSeconds),
});

// Keep-alive target for the SmarterASP scheduled task (every 10 min) and for uptime checks.
app.MapGet("/health", (ProcessLifetime life, ConnectionRegistry registry) => Results.Ok(new
{
    ok = true,
    uptimeSec = (long)life.Uptime.TotalSeconds,
    connected = registry.Count,
    utc = DateTime.UtcNow,
}));

// Operator view: what is connected, what each charger last said, and how often the host restarts us.
app.MapGet("/status", (HttpContext http, ProcessLifetime life, ConnectionRegistry registry, RawMessageStore store) =>
{
    if (string.IsNullOrEmpty(ocpp.StatusApiKey) || http.Request.Headers["X-Api-Key"] != ocpp.StatusApiKey)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        version = life.Version,
        environment = app.Environment.EnvironmentName,
        startedAt = life.StartedAt,
        uptimeSec = (long)life.Uptime.TotalSeconds,
        previousStartAt = life.PreviousStartAt,
        startsLast7Days = life.StartsLast7Days,
        rawMessages = new { databaseEnabled = store.DatabaseEnabled, written = store.Written, failed = store.Failed },
        options = new { ocpp.HeartbeatIntervalSeconds, ocpp.PingIntervalSeconds, ocpp.RequireSubprotocol, ocpp.AuthorizeMode },
        connected = registry.All.Select(s => new
        {
            s.ChargePointId,
            s.OcppChargePointId,
            s.ChargingPointId,
            s.RemoteIp,
            s.Subprotocol,
            s.ConnectedAt,
            s.LastMessageAt,
            secondsSinceLastMessage = (long)(DateTime.UtcNow - s.LastMessageAt).TotalSeconds,
            s.MessagesIn,
            s.MessagesOut,
            s.LastAction,
            s.Vendor,
            s.Model,
            s.FirmwareVersion,
            s.SerialNumber,
            s.LastBootAt,
            s.HeartbeatInterval,
            connectors = s.Connectors.OrderBy(c => c.Key).Select(c => new
            {
                connectorId = c.Key,
                c.Value.Status,
                c.Value.ErrorCode,
                c.Value.Info,
                c.Value.ChargerTimestamp,
                c.Value.ReceivedAt,
            }),
        }).OrderBy(s => s.ChargePointId),
    });
});

// Phase 2 — central-system commands. The API (not the admin browser) calls this with the
// same shared key as /status: POST /commands/{chargePointId} { action, payload, timeoutSeconds? }
// → { status, payload, errorCode, errorDescription, elapsedMs }. status = Answered means the
// charger replied; its own verdict (Accepted / Rejected / …) is inside payload.
app.MapPost("/commands/{chargePointId}", async (HttpContext http, string chargePointId, CommandRequest body, CommandSender sender, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(ocpp.StatusApiKey) || http.Request.Headers["X-Api-Key"] != ocpp.StatusApiKey)
        return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Action))
        return Results.BadRequest(new { error = "action is required" });

    var timeout = body.TimeoutSeconds is > 0 and <= 120 ? TimeSpan.FromSeconds(body.TimeoutSeconds.Value) : (TimeSpan?)null;
    var outcome = await sender.SendAsync(chargePointId, body.Action, body.Payload, timeout, ct);
    return Results.Ok(outcome);
});

// The OCPP 1.6J endpoint. Chargers are configured with  wss://host/ocpp16/  and append their own id.
app.Map("/ocpp16/{chargePointId}", async (HttpContext context, string chargePointId, OcppWebSocketHandler handler) =>
{
    await handler.HandleAsync(context, chargePointId);
});

app.MapGet("/", () => Results.Text("Cable OCPP 1.6J central system. Chargers connect to /ocpp16/{chargePointId}.", "text/plain"));

app.Run();

/// <summary>Body of POST /commands/{chargePointId}.</summary>
public sealed record CommandRequest(string Action, System.Text.Json.JsonElement Payload, int? TimeoutSeconds);
