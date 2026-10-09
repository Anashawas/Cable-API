using Cable.Core.Constants;
using Domain.Enitites;

namespace Application.Ocpp.Queries;

public record OcppConnectorDto(
    int Id,
    int ConnectorId,
    string Status,
    string ErrorCode,
    string? VendorErrorCode,
    string? Info,
    DateTime? StatusUpdatedAt,
    DateTime StatusReceivedAt,
    int? PlugTypeId,
    string? PlugTypeName,
    decimal? PowerKw,
    bool IsFree,
    bool IsOccupied)
{
    public static OcppConnectorDto From(OcppConnector c, string? plugTypeName) => new(
        c.Id, c.ConnectorId, c.Status, c.ErrorCode, c.VendorErrorCode, c.Info, c.StatusUpdatedAt, c.StatusReceivedAt,
        c.PlugTypeId, plugTypeName, c.PowerKw,
        OcppConnectorStatus.IsFree(c.Status), OcppConnectorStatus.IsOccupied(c.Status));
}

public record OcppTransactionDto(
    int Id,
    int ConnectorId,
    string IdTag,
    DateTime StartedAt,
    DateTime? StoppedAt,
    int? DurationSec,
    long MeterStartWh,
    long? MeterStopWh,
    decimal? EnergyKwh,
    string? StopReason,
    bool IsOpen,
    bool IsStale,
    bool IsOrphan,
    bool WasRejected,
    /// <summary>Card | App | Operator.</summary>
    string StartSource = "Card",
    int? StartedByUserId = null,
    /// <summary>Human wording for StopReason (vendor quirks folded in).</summary>
    string? StopReasonText = null,
    /// <summary>Price under the time-of-use tariff (fils / JOD); null while open or when the energy is unknown.</summary>
    int? CostFils = null,
    decimal? CostJod = null,
    int? TariffVersion = null)
{
    public static OcppTransactionDto From(OcppTransaction t, DateTime nowUtc) => new(
        t.Id, t.ConnectorId, t.IdTag, t.StartedAt, t.StoppedAt,
        (int)((t.StoppedAt ?? nowUtc) - t.StartedAt).TotalSeconds,
        t.MeterStartWh, t.MeterStopWh, t.EnergyKwh, t.StopReason, t.IsOpen, t.IsStale, t.IsOrphan, t.WasRejected,
        t.StartSource, t.StartedByUserId, OcppStopReason.Describe(t.StopReason),
        t.CostFils, t.CostFils is int f ? f / 1000m : null, t.TariffVersion);
}

/// <summary>One row of the admin charger list.</summary>
public record OcppChargePointListItemDto(
    int Id,
    string ChargePointId,
    string? DisplayName,
    int ChargingPointId,
    string StationName,
    string? Vendor,
    string? Model,
    string? FirmwareVersion,
    bool IsEnabled,
    bool IsConnected,
    /// <summary>Online | Reconnecting | Offline — see OcppLiveness.</summary>
    string ConnectionState,
    DateTime? ConnectedAt,
    DateTime? DisconnectedAt,
    DateTime? LastMessageAt,
    DateTime? LastBootAt,
    bool HasPassword,
    DateTime? LockedUntil,
    int ConnectorCount,
    int FreeConnectors,
    int FaultedConnectors,
    int OpenSessions,
    OcppSubscriptionStateDto Subscription,
    DateTime CreatedAt,
    /// <summary>Only until the first BootNotification: Waiting | Connected | Refused. Null once the unit has booted.</summary>
    string? OnboardingState = null,
    string? OnboardingReason = null,
    /// <summary>N-6: % of the last 30 days reachable and fault-free; null until the daily job ran.</summary>
    decimal? ReliabilityPct = null);

/// <summary>N-6 breakdown for one charger (admin + partner).</summary>
public record OcppReliabilityDto(decimal? Pct, decimal? OnlinePct, decimal? FaultFreePct, int? OfflineIncidents, int? FaultIncidents, DateTime? ComputedAt, int WindowDays, bool Reliable);

public record OcppChargePointTodayDto(int Sessions, decimal EnergyKwh, int Faults);

/// <summary>
/// What happened since the charger was registered, for the admin standing at the station:
/// Waiting (nothing reached us yet) · Connected (socket accepted, no BootNotification yet) ·
/// Refused (handshake rejected — Reason says why: unknown id, bad password, disabled, locked) ·
/// Booted (vendor / model / firmware are in). At = when that last happened.
/// </summary>
public record OcppOnboardingDto(string State, string? Reason, int? HttpStatus, DateTime? At, DateTime RegisteredAt);

public record OcppChargePointDetailDto(
    int Id,
    string ChargePointId,
    string? DisplayName,
    int ChargingPointId,
    string StationName,
    string? Vendor,
    string? Model,
    string? FirmwareVersion,
    string? SerialNumber,
    string? ChargeBoxSerialNumber,
    string? Iccid,
    string? Imsi,
    string? MeterSerialNumber,
    int HeartbeatInterval,
    bool IsEnabled,
    bool IsConnected,
    string ConnectionState,
    DateTime? ConnectedAt,
    DateTime? DisconnectedAt,
    DateTime? LastBootAt,
    DateTime? LastMessageAt,
    string? LastRemoteIp,
    bool HasPassword,
    int FailedAuthCount,
    DateTime? LockedUntil,
    string UrlPath,
    string? WebSocketBaseUrl,
    int? Port,
    OcppSubscriptionStateDto Subscription,
    List<OcppConnectorDto> Connectors,
    List<OcppTransactionDto> RecentTransactions,
    OcppChargePointTodayDto Today,
    OcppLocalListStateDto LocalList,
    OcppOnboardingDto Onboarding,
    OcppReliabilityDto Reliability,
    DateTime CreatedAt,
    DateTime? ModifiedAt);

/// <summary>State of the card list pushed into the unit. Status: Synced | Pending | Failed | NotSupported; null = never pushed.</summary>
public record OcppLocalListStateDto(string? Status, int? Version, DateTime? SyncedAt, int CardsAtStation, bool Confirmed);

public record OcppRawMessageDto(
    long Id,
    string Direction,
    byte? MessageType,
    string? MessageId,
    string? Action,
    string? Payload,
    string? RemoteIp,
    DateTime CreatedAt);

/// <summary>Admin dashboard tile: the whole fleet in one call.</summary>
public record OcppFleetHealthDto(
    int TotalChargers,
    int Enabled,
    int Online,
    int Reconnecting,
    int Offline,
    int NeverConnected,
    int Locked,
    int TotalConnectors,
    int FreeConnectors,
    int FaultedConnectors,
    int OpenSessions,
    int StaleSessions,
    int SessionsToday,
    decimal EnergyTodayKwh,
    int StationsWithChargers,
    int StationsWithoutActiveSubscription,
    int OpenAlerts);

public record OcppAuthorizedTagDto(
    int Id,
    int ChargingPointId,
    string IdTag,
    string? Label,
    bool IsEnabled,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

// ---------------------------------------------------------------------------
// Phase 2 — central-system commands
// ---------------------------------------------------------------------------

/// <summary>
/// Outcome of one command. <see cref="Status"/> is the transport outcome (Answered |
/// CallError | NotConnected | Timeout | Disconnected | Invalid | Unreachable);
/// <see cref="ResultStatus"/> is the unit's own word (Accepted, Rejected, Scheduled,
/// Unlocked, UnlockFailed, NotSupported, RebootRequired…); <see cref="Accepted"/>
/// folds both into "did it work".
/// </summary>
public record OcppCommandResultDto(
    int CommandId,
    string Action,
    string Status,
    string? ResultStatus,
    string? ResponsePayload,
    string? ErrorCode,
    string? ErrorDescription,
    long ElapsedMs,
    bool Accepted);

public record OcppCommandDto(
    int Id,
    string Action,
    string RequestPayload,
    string Status,
    string? ResultStatus,
    string? ResponsePayload,
    string? ErrorCode,
    string? ErrorDescription,
    int? DurationMs,
    int? RequestedById,
    string? RequestedByName,
    DateTime CreatedAt,
    /// <summary>When the charger's follow-up message proved the command took effect (Reset → BootNotification, ChangeAvailability → StatusNotification…). Null = accepted but not yet confirmed.</summary>
    DateTime? CompletedAt = null,
    int? ConfirmedAfterSec = null);

/// <summary>An alert-job finding. Type: ChargerOffline | ConnectorFaulted | SessionTooLong; ResolvedAt null = still open.</summary>
public record OcppAlertDto(
    int Id,
    string Type,
    int OcppChargePointId,
    string ChargePointId,
    string? DisplayName,
    int ChargingPointId,
    string StationName,
    int? ConnectorId,
    int? OcppTransactionId,
    DateTime ConditionSince,
    DateTime NotifiedAt,
    DateTime? ResolvedAt,
    string? Details,
    int Recipients,
    /// <summary>ParkedAfterCharging: the driver told first (null = card not linked to a user).</summary>
    int? DriverUserId = null,
    /// <summary>ParkedAfterCharging: when the station was told.</summary>
    DateTime? EscalatedAt = null);
