using System.Security.Claims;
using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Ocpp.Live;
using Cable.Core.Constants;
using Cable.Core.Extenstions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Cable.Hubs;

/// <summary>
/// Live Cable Connect updates for the web (and later the apps): a page joins the station it shows and
/// receives "ocpp" messages (<see cref="OcppLiveEvent"/>) the moment a charger reports a change — then
/// refetches what it displays. Who may join a station is decided like the station's REST endpoints:
/// admins, the owner, and workers with the ConnectView privilege. The pages keep a slow poll as fallback.
/// Connect: /hubs/ocpp?access_token={JWT} (WebSockets, falling back to SSE / long polling).
/// </summary>
[Authorize]
public sealed class OcppLiveHub(IApplicationDbContext db) : Hub<IOcppLiveClient>
{
    public const string Path = "/hubs/ocpp";

    public async Task JoinStation(int chargingPointId)
    {
        if (chargingPointId <= 0) throw new HubException("Invalid station.");
        var user = new HubCurrentUser(Context.User);
        try
        {
            if (!await AdminRoleGuard.IsAdminAsync(db, user, Context.ConnectionAborted))
                await ProviderAccessGuard.EnsureCanActForProviderAsync(db, user, ProviderAccessGuard.ChargingPoint, chargingPointId,
                    Context.ConnectionAborted, requiredPrivilege: WorkerPrivileges.ConnectView);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HubException("You are not allowed to watch this station.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, OcppLiveGroups.Station(chargingPointId), Context.ConnectionAborted);
    }

    public Task LeaveStation(int chargingPointId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OcppLiveGroups.Station(chargingPointId), Context.ConnectionAborted);

    /// <summary>Admins only: every station's events (dashboard, Cable Connect list).</summary>
    public async Task JoinFleet()
    {
        if (!await AdminRoleGuard.IsAdminAsync(db, new HubCurrentUser(Context.User), Context.ConnectionAborted))
            throw new HubException("Admins only.");
        await Groups.AddToGroupAsync(Context.ConnectionId, OcppLiveGroups.Admin, Context.ConnectionAborted);
    }

    public Task LeaveFleet() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OcppLiveGroups.Admin, Context.ConnectionAborted);

    /// <summary>The caller as the guards expect it — from the hub connection's claims, not IHttpContextAccessor.</summary>
    private sealed class HubCurrentUser(ClaimsPrincipal? principal) : ICurrentUserService
    {
        public int? UserId => principal?.FindFirstValue(ClaimTypes.NameIdentifier).AsInt();
        public string Token => "";
        public string App => AuthApps.NormalizeOrDefault(principal?.FindFirstValue(AuthApps.ClaimType));
    }
}
