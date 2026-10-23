namespace Application.Ocpp.Live;

/// <summary>
/// A "something changed" hint from Cable.Ocpp, pushed to the pages watching the station over SignalR
/// (hub method "ocpp"). Pages refetch what they show; the hint only says what and where.
/// </summary>
/// <param name="Type">connector | session | charger (OcppLiveEventTypes).</param>
/// <param name="Status">connector: the OCPP status (Available, Preparing, Charging, Faulted…);
/// session: started | updated | stopped | limit-reached; charger: online | offline | booted.</param>
public record OcppLiveEvent(
    string Type,
    int ChargingPointId,
    int ChargerId,
    int? ConnectorId,
    int? TransactionId,
    string? Status,
    DateTime At);

public static class OcppLiveEventTypes
{
    public const string Connector = "connector";
    public const string Session = "session";
    public const string Charger = "charger";
}

public static class OcppLiveGroups
{
    /// <summary>Everyone allowed to watch one station (owner, managers with ConnectView, admins who opened it).</summary>
    public static string Station(int chargingPointId) => $"station-{chargingPointId}";

    /// <summary>Admins watching the whole fleet (dashboard, Cable Connect list).</summary>
    public const string Admin = "ocpp-admin";

    /// <summary>The SignalR client method the pages listen on (IOcppLiveClient.Ocpp; matched case-insensitively).</summary>
    public const string ClientMethod = "ocpp";
}
