using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cable.Ocpp.Transport;

/// <summary>
/// Keeps OcppChargePoint's live-connection columns in step with the socket. Set-based
/// updates, no tracking: these run on every connect/disconnect and (throttled) on
/// every message. Connector rows are deliberately never touched here (R5).
/// </summary>
public sealed class ConnectionStateService(IApplicationDbContext db)
{
    public Task MarkConnectedAsync(int ocppChargePointId, string? remoteIp, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return db.OcppChargePoints
            .Where(c => c.Id == ocppChargePointId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.IsConnected, true)
                .SetProperty(c => c.ConnectedAt, now)
                .SetProperty(c => c.LastMessageAt, now)
                .SetProperty(c => c.LastRemoteIp, remoteIp), cancellationToken);
    }

    public Task MarkDisconnectedAsync(int ocppChargePointId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return db.OcppChargePoints
            .Where(c => c.Id == ocppChargePointId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.IsConnected, false)
                .SetProperty(c => c.DisconnectedAt, now), cancellationToken);
    }

    /// <summary>
    /// Persists LastMessageAt and reports whether the charger is still allowed to stay
    /// connected — an admin may have disabled or deleted it since the handshake.
    /// </summary>
    public async Task<bool> TouchAsync(int ocppChargePointId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await db.OcppChargePoints
            .Where(c => c.Id == ocppChargePointId && c.IsEnabled && !c.IsDeleted)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.LastMessageAt, now), cancellationToken);
        return updated > 0;
    }
}
