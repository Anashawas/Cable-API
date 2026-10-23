using System.Collections.Concurrent;
using Application.Common.Interfaces;
using Cable.Core.Constants;
using Hangfire;

namespace Cable.Ocpp.Services;

/// <summary>
/// Faulted → push + inbox to the station's owner and managers. The work itself runs
/// in the API's Hangfire server (NotifyOcppFaultAsync), which has Firebase; this
/// process only enqueues. De-duplicated so a charger flapping between Faulted and
/// Available does not page the owner every 15 seconds.
/// </summary>
public sealed class FaultNotifier(IBackgroundJobClient jobs, ILogger<FaultNotifier> log)
{
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new();

    /// <summary>
    /// The alert waits this long so every plug that faults in the same event (a power drop faults
    /// both plugs, often twice: OtherError then NoError) goes out as one message per charger.
    /// </summary>
    internal static readonly TimeSpan GroupDelay = TimeSpan.FromSeconds(10);

    public void Report(int ocppConnectorId, string chargePointId, int connectorId, string errorCode, string? info)
    {
        // One alert per charger per window: the job lists every plug faulted at send time.
        var key = chargePointId;
        var now = DateTime.UtcNow;
        if (_lastSent.TryGetValue(key, out var last) && now - last < OcppLimits.FaultNotificationDedupe)
            return;
        _lastSent[key] = now;

        try
        {
            jobs.Schedule<IBackgroundJobService>(s => s.NotifyOcppFaultAsync(ocppConnectorId, errorCode, info, CancellationToken.None), GroupDelay);
            log.LogWarning("FAULT {ChargePointId} connector {ConnectorId}: {ErrorCode} {Info} — notification enqueued",
                chargePointId, connectorId, errorCode, info);
        }
        catch (Exception ex)
        {
            // Hangfire storage unreachable: the fault is still in OcppConnector and the raw log.
            _lastSent.TryRemove(key, out _);
            log.LogError(ex, "FAULT {ChargePointId} connector {ConnectorId}: could not enqueue notification", chargePointId, connectorId);
        }
    }
}
