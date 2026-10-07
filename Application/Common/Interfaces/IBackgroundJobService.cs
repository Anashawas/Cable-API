namespace Application.Common.Interfaces;

public interface IBackgroundJobService
{
    // ==========================================
    // Transaction Code Expiry
    // ==========================================
    Task<int> ExpireOfferTransactionCodesAsync(CancellationToken cancellationToken = default);
    Task<int> ExpirePartnerTransactionCodesAsync(CancellationToken cancellationToken = default);

    /// <summary>Switches off subscriptions whose AfterDays grace has run out. Manual-grace ones are never touched.</summary>
    Task<int> ApplySubscriptionGraceAsync(CancellationToken cancellationToken = default);

    // ==========================================
    // Security Cleanup (Critical)
    // ==========================================
    Task<int> CleanupExpiredPhoneVerificationsAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredPasswordResetsAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredOtpRateLimitsAsync(CancellationToken cancellationToken = default);

    // ==========================================
    // Business Expiry (Important)
    // ==========================================
    Task<int> DeactivateExpiredOffersAsync(CancellationToken cancellationToken = default);
    Task<int> DeactivateExpiredSharedLinksAsync(CancellationToken cancellationToken = default);
    Task<int> EndExpiredLoyaltySeasonsAsync(CancellationToken cancellationToken = default);
    Task<int> ExpireLoyaltyPointsAsync(CancellationToken cancellationToken = default);
    Task<int> DeactivateExpiredRewardsAsync(CancellationToken cancellationToken = default);
    Task<int> UnblockExpiredLoyaltyBlocksAsync(CancellationToken cancellationToken = default);
    Task<int> UnblockExpiredProviderLoyaltyBlocksAsync(CancellationToken cancellationToken = default);

    // ==========================================
    // Cable Connect (OCPP)
    // ==========================================
    /// <summary>Flags open sessions with no StopTransaction for 24 h (R6 keeps them open; this just marks them for reconciliation).</summary>
    Task<int> MarkStaleOcppTransactionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Trims OcppRawMessage to OcppLimits.RawMessageRetentionDays, in batches.</summary>
    Task<int> PurgeOcppRawMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Enqueued by Cable.Ocpp on a Faulted StatusNotification: push + inbox to the station owner and active managers.</summary>
    Task NotifyOcppFaultAsync(int ocppConnectorId, string errorCode, string? info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the station's allowed cards into its chargers (SendLocalList Full + ClearCache) so they
    /// authorize offline. Enqueued after every card change and by Cable.Ocpp when a Pending unit boots.
    /// <paramref name="ocppChargePointId"/> limits the push to one charger (manual "sync now").
    /// Returns the number of chargers that confirmed the list.
    /// </summary>
    Task<int> SyncOcppLocalListAsync(int chargingPointId, int? ocppChargePointId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every 5 min: opens an OcppAlert (+ push and inbox to admins, the station owner and managers)
    /// when a charger is offline, a plug Faulted or a session open longer than the OcppLimits
    /// thresholds; closes it (and tells them) when the condition clears. Returns alerts opened.
    /// </summary>
    Task<int> CheckOcppAlertsAsync(CancellationToken cancellationToken = default);
}
