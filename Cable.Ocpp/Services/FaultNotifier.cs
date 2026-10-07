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

    public void Report(int ocppConnectorId, string chargePointId, int connectorId, string errorCode, string? info)
    {
        var key = $"{chargePointId}:{connectorId}:{errorCode}";
        var now = DateTime.UtcNow;
        if (_lastSent.TryGetValue(key, out var last) && now - last < OcppLimits.FaultNotificationDedupe)
            return;
        _lastSent[key] = now;

        try
        {
            jobs.Enqueue<IBackgroundJobService>(s => s.NotifyOcppFaultAsync(ocppConnectorId, errorCode, info, CancellationToken.None));
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
