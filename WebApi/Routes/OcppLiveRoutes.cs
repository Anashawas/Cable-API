using System.Security.Cryptography;
using System.Text;
using Application.Ocpp.Live;
using Cable.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Cable.Routes;

/// <summary>
/// Cable.Ocpp → API: live events to push to the web over SignalR (<see cref="OcppLiveHub"/>).
/// Machine-to-machine: no user login; the shared key is OcppServer:ApiKey (the same secret the API
/// uses to call Cable.Ocpp's /commands), sent as X-Api-Key.
/// </summary>
public static class OcppLiveRoutes
{
    public static IEndpointRouteBuilder MapOcppLiveRoutes(this IEndpointRouteBuilder app)
    {
        // Close the socket when the JWT expires instead of letting a connection outlive its login;
        // the client reconnects with a fresh token (or gets 401 and goes to login).
        app.MapHub<OcppLiveHub>(OcppLiveHub.Path, o => o.CloseOnAuthenticationExpiration = true);

        app.MapPost("/api/internal/ocpp/events", async (HttpContext http, List<OcppLiveEvent> events,
                IHubContext<OcppLiveHub, IOcppLiveClient> hub, IConfiguration config, CancellationToken ct) =>
            {
                var expected = config["OcppServer:ApiKey"];
                var given = http.Request.Headers["X-Api-Key"].ToString();
                if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(expected, given))
                    return Results.Unauthorized();

                foreach (var e in events.Where(e => e.ChargingPointId > 0).Take(500))
                    await hub.Clients.Groups(OcppLiveGroups.Station(e.ChargingPointId), OcppLiveGroups.Admin).Ocpp(e);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    // Hash first so the comparison is fixed-time even when the lengths differ.
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
