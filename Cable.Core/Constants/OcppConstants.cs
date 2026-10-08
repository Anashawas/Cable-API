namespace Cable.Core.Constants;

/// <summary>OCPP 1.6 ChargePointStatus values (StatusNotification.req.status).</summary>
public static class OcppConnectorStatus
{
    public const string Available = "Available";
    public const string Preparing = "Preparing";
    public const string Charging = "Charging";
    public const string SuspendedEVSE = "SuspendedEVSE";
    public const string SuspendedEV = "SuspendedEV";
    public const string Finishing = "Finishing";
    public const string Reserved = "Reserved";
    public const string Unavailable = "Unavailable";
    public const string Faulted = "Faulted";

    /// <summary>What the B2C "N of M free" badge counts as free.</summary>
    public static bool IsFree(string? status) => status == Available;

    /// <summary>A car is attached (any of the in-session states).</summary>
    public static bool IsOccupied(string? status) =>
        status is Preparing or Charging or SuspendedEVSE or SuspendedEV or Finishing;
}

/// <summary>OCPP 1.6 AuthorizationStatus values (idTagInfo.status).</summary>
public static class OcppAuthorizationStatus
{
    public const string Accepted = "Accepted";
    public const string Blocked = "Blocked";
    public const string Expired = "Expired";
    public const string Invalid = "Invalid";
    public const string ConcurrentTx = "ConcurrentTx";
}

/// <summary>Cable.Ocpp's Authorize policy (Ocpp:AuthorizeMode).</summary>
public static class OcppAuthorizeMode
{
    /// <summary>Accepted only for tags in OcppAuthorizedTag for that station. Production default.</summary>
    public const string List = "List";

    /// <summary>Everything Accepted — lab / simulator only. Never on a live station.</summary>
    public const string AcceptAll = "AcceptAll";

    /// <summary>Everything Invalid — connection tests on a live station where nobody should charge through us.</summary>
    public const string RejectAll = "RejectAll";
}

/// <summary>OcppChargePoint.LocalListStatus — state of the card list pushed into the unit.</summary>
public static class OcppLocalListStatus
{
    /// <summary>The unit holds the station's current list (LocalListVersion / LocalListSyncedAt say which).</summary>
    public const string Synced = "Synced";

    /// <summary>Cards changed (or the unit was offline); a push is due at the next boot / job run.</summary>
    public const string Pending = "Pending";

    /// <summary>The unit answered Failed / VersionMismatch or did not answer; retried on next change or boot.</summary>
    public const string Failed = "Failed";

    /// <summary>The unit has no LocalAuthListManagement profile; never retried automatically.</summary>
    public const string NotSupported = "NotSupported";
}

/// <summary>OcppAlert.Type — what the 5-minute alert job watches.</summary>
public static class OcppAlertType
{
    public const string ChargerOffline = "ChargerOffline";
    public const string ConnectorFaulted = "ConnectorFaulted";
    public const string SessionTooLong = "SessionTooLong";

    /// <summary>Charging finished (Finishing / SuspendedEV) but the cable is still in: the plug is blocked for the next driver.</summary>
    public const string ParkedAfterCharging = "ParkedAfterCharging";
}

/// <summary>
/// Configuration keys an operator must never change from a screen: anything that tells the
/// unit where its central system is or who it is. A wrong value here sends the charger to
/// another server (or nowhere) and only a site visit brings it back. Enforced in the API
/// command and again in Cable.Ocpp, so no caller of the internal endpoint can bypass it.
/// </summary>
public static class OcppProtectedConfigurationKeys
{
    private static readonly string[] Fragments =
    [
        "CentralSystem", "Url", "Uri", "Endpoint", "Backend", "Host",
        "ChargePointId", "ChargeBoxId", "ChargeBoxIdentity", "Identity", "ChargePointSerial",
        "AuthorizationKey", "SecurityProfile", "Password", "Certificate", "Apn", "Sim",
    ];

    public static bool IsProtected(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        foreach (var f in Fragments)
            if (key.Contains(f, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

public static class OcppLimits
{
    /// <summary>Open transaction with no StopTransaction for this long is flagged IsStale.</summary>
    public static readonly TimeSpan StaleTransactionAfter = TimeSpan.FromHours(24);

    /// <summary>Raw frame retention.</summary>
    public const int RawMessageRetentionDays = 30;

    /// <summary>OCPP 1.6 SendLocalList: a Full update bigger than the unit's SendLocalListMaxLength is refused; 100 is a safe floor for the pilot hardware.</summary>
    public const int LocalListMaxEntries = 100;

    /// <summary>Alert job thresholds. A charger offline / a plug Faulted longer than this, a session open longer than this → push + inbox.</summary>
    public static readonly TimeSpan OfflineAlertAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FaultedAlertAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LongSessionAlertAfter = TimeSpan.FromHours(6);

    /// <summary>Plug in Finishing / SuspendedEV this long → tell the driver (if the card is linked to a user), else the station.</summary>
    public static readonly TimeSpan ParkedAlertAfter = TimeSpan.FromMinutes(20);

    /// <summary>Still parked this long after the driver was told → tell the station owner / managers.</summary>
    public static readonly TimeSpan ParkedEscalateAfter = TimeSpan.FromMinutes(20);

    /// <summary>Commands to one charger per minute; above this the API refuses with a clear message (a nervous click-storm must not reboot a unit five times).</summary>
    public const int CommandsPerChargerPerMinute = 10;

    /// <summary>An identical command (same action + payload) to the same charger is refused while the previous one is still unconfirmed and younger than this.</summary>
    public static readonly TimeSpan DuplicateCommandWindow = TimeSpan.FromSeconds(90);

    /// <summary>Commands whose effect the unit proves with a later message — the ones duplicate protection applies to.</summary>
    public static readonly string[] ConfirmableCommands = ["Reset", "ChangeAvailability", "UnlockConnector", "RemoteStopTransaction"];

    /// <summary>Same connector + same error code within this window = one notification.</summary>
    public static readonly TimeSpan FaultNotificationDedupe = TimeSpan.FromMinutes(30);
}
