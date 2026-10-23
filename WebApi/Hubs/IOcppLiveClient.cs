using Application.Ocpp.Live;

namespace Cable.Hubs;

/// <summary>
/// What <see cref="OcppLiveHub"/> calls on connected pages — strongly typed so the server can't
/// send a misspelled method. The wire name is the method name ("Ocpp"); SignalR clients match it
/// case-insensitively, so pages listen with <c>connection.on("ocpp", …)</c>.
/// </summary>
public interface IOcppLiveClient
{
    Task Ocpp(OcppLiveEvent e);
}
