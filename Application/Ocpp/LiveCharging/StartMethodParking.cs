using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>
/// §11.d — how a session was started. The remote start only reaches the charger as an idTag, so the API parks the
/// finer method ("Sticker") on the plug, and Cable.Ocpp's StartTransaction takes it into the new session — the same
/// hand-over as the pending charge limit. Everything else is the session's StartSource.
/// </summary>
public static class StartMethodParking
{
    /// <summary>The method to show for a row: the stored one, else (rows from before the column) the source.</summary>
    public static string Of(string? startMethod, string startSource) => startMethod ?? startSource;

    /// <summary>
    /// API side, before the RemoteStartTransaction: park <paramref name="method"/> (Sticker) on the plug, or clear
    /// a stale one when this start is a plain app start (null). Saves only when something changes.
    /// </summary>
    public static async Task ParkAsync(IApplicationDbContext db, int ocppChargePointId, int connectorId, string? method, CancellationToken ct)
    {
        var plug = await db.OcppConnectors.FirstOrDefaultAsync(x => x.OcppChargePointId == ocppChargePointId && x.ConnectorId == connectorId, ct);
        if (plug is null) return;
        if (method is null && plug.PendingStartMethod is null) return;
        plug.PendingStartMethod = method;
        plug.PendingStartMethodAt = method is null ? null : DateTime.UtcNow;
        await db.SaveChanges(ct);
    }

    /// <summary>
    /// Cable.Ocpp side, in StartTransaction before the row is saved: sets <see cref="OcppTransaction.StartMethod"/> —
    /// the parked method for an accepted App start on that plug within OcppVirtualTag.RemoteStartAuthorizeWindow,
    /// else the StartSource. The parked value is cleared once taken (saved with the session).
    /// </summary>
    public static async Task ApplyAsync(IApplicationDbContext db, OcppTransaction tx, DateTime nowUtc, CancellationToken ct)
    {
        tx.StartMethod = tx.StartSource;
        if (tx.WasRejected || tx.StartSource != OcppStartSource.App) return;

        var since = nowUtc - OcppVirtualTag.RemoteStartAuthorizeWindow;
        var plug = await db.OcppConnectors.FirstOrDefaultAsync(c => c.OcppChargePointId == tx.OcppChargePointId && c.ConnectorId == tx.ConnectorId
            && c.PendingStartMethod != null && c.PendingStartMethodAt >= since, ct);
        if (plug is null) return;

        tx.StartMethod = plug.PendingStartMethod;
        plug.PendingStartMethod = null;
        plug.PendingStartMethodAt = null;
    }
}
