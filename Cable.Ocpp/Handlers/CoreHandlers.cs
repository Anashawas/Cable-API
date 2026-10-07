using System.Text.Json;
using Application.Common.Interfaces;
using Cable.Core.Constants;
using Cable.Ocpp.Services;
using Cable.Ocpp.Transport;
using Hangfire;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Cable.Ocpp.Handlers;

/// <summary>Updates the charger row with what the unit says about itself; never touches sessions (R2).</summary>
public sealed class BootNotificationHandler(IApplicationDbContext db, Hangfire.IBackgroundJobClient jobs, ILogger<BootNotificationHandler> log) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cp = await db.OcppChargePoints.FirstAsync(c => c.Id == session.OcppChargePointId, cancellationToken);

        cp.Vendor = OcppPayload.Str(payload, "chargePointVendor") ?? cp.Vendor;
        cp.Model = OcppPayload.Str(payload, "chargePointModel") ?? cp.Model;
        cp.FirmwareVersion = OcppPayload.Str(payload, "firmwareVersion") ?? cp.FirmwareVersion;
        cp.SerialNumber = OcppPayload.Str(payload, "chargePointSerialNumber") ?? cp.SerialNumber;
        cp.ChargeBoxSerialNumber = OcppPayload.Str(payload, "chargeBoxSerialNumber") ?? cp.ChargeBoxSerialNumber;
        cp.Iccid = OcppPayload.Str(payload, "iccid") ?? cp.Iccid;
        cp.Imsi = OcppPayload.Str(payload, "imsi") ?? cp.Imsi;
        cp.MeterSerialNumber = OcppPayload.Str(payload, "meterSerialNumber") ?? cp.MeterSerialNumber;
        cp.LastBootAt = now;
        await db.SaveChanges(cancellationToken);

        session.Vendor = cp.Vendor;
        session.Model = cp.Model;
        session.FirmwareVersion = cp.FirmwareVersion;
        session.SerialNumber = cp.SerialNumber;
        session.LastBootAt = now;
        session.HeartbeatInterval = cp.HeartbeatInterval;

        log.LogInformation("BOOT {ChargePointId}: {Vendor} {Model} fw {Firmware} sn {Serial}",
            session.ChargePointId, cp.Vendor, cp.Model, cp.FirmwareVersion, cp.SerialNumber);

        // Cards changed while it was offline (or a push failed): the API's job pushes the list now that it is back.
        if (cp.LocalListStatus is Cable.Core.Constants.OcppLocalListStatus.Pending or Cable.Core.Constants.OcppLocalListStatus.Failed)
        {
            try
            {
                var stationId = cp.ChargingPointId;
                var chargerId = cp.Id;
                jobs.Schedule<IBackgroundJobService>(s => s.SyncOcppLocalListAsync(stationId, chargerId, CancellationToken.None), TimeSpan.FromSeconds(5));
                log.LogInformation("BOOT {ChargePointId}: local list {Status} — sync enqueued", session.ChargePointId, cp.LocalListStatus);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "BOOT {ChargePointId}: could not enqueue local list sync", session.ChargePointId);
            }
        }

        // currentTime sets the charger's clock — real UTC, no local conversion (R7).
        return new { status = "Accepted", currentTime = now, interval = cp.HeartbeatInterval };
    }
}

public sealed class HeartbeatHandler : IOcppHandler
{
    public Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken) =>
        Task.FromResult<object>(new { currentTime = DateTime.UtcNow });
}

/// <summary>Upserts the connector row; unknown connector ids are created, never refused. Faulted → owner notification.</summary>
public sealed class StatusNotificationHandler(IApplicationDbContext db, FaultNotifier faults) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var connectorId = OcppPayload.Int(payload, "connectorId") ?? 0;
        var status = OcppPayload.Str(payload, "status") ?? "Unknown";
        var errorCode = OcppPayload.Str(payload, "errorCode") ?? "NoError";
        var info = OcppPayload.Str(payload, "info");
        var vendorErrorCode = OcppPayload.Str(payload, "vendorErrorCode");
        var chargerTime = OcppPayload.Date(payload, "timestamp");

        var connector = await db.OcppConnectors
            .FirstOrDefaultAsync(c => c.OcppChargePointId == session.OcppChargePointId && c.ConnectorId == connectorId, cancellationToken);
        if (connector is null)
        {
            connector = new OcppConnector { OcppChargePointId = session.OcppChargePointId, ConnectorId = connectorId };
            db.OcppConnectors.Add(connector);
        }

        connector.Status = status;
        connector.ErrorCode = errorCode;
        connector.Info = info;
        connector.VendorErrorCode = vendorErrorCode;
        connector.StatusUpdatedAt = chargerTime;
        connector.StatusReceivedAt = now;
        await db.SaveChanges(cancellationToken);

        session.Connectors[connectorId] = new ConnectorState(status, errorCode, info, chargerTime, now);

        if (status == OcppConnectorStatus.Faulted)
            faults.Report(connector.Id, session.ChargePointId, connectorId, errorCode, info);

        return new { };
    }
}

public sealed class AuthorizeHandler(TagAuthorizer authorizer) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var status = await authorizer.AuthorizeAsync(session, OcppPayload.Str(payload, "idTag"), cancellationToken);
        return new { idTagInfo = new { status } };
    }
}

/// <summary>Opens the session; the row's identity is the transactionId the charger will quote from now on.</summary>
public sealed class StartTransactionHandler(IApplicationDbContext db, TagAuthorizer authorizer) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var idTag = OcppPayload.Str(payload, "idTag") ?? "";
        var status = await authorizer.AuthorizeAsync(session, idTag, cancellationToken);

        // A rejected start still gets a row: the spec requires a transactionId in the reply,
        // and the charger's StopTransaction a moment later must find something to close.
        var tx = new OcppTransaction
        {
            OcppChargePointId = session.OcppChargePointId,
            ConnectorId = OcppPayload.Int(payload, "connectorId") ?? 0,
            IdTag = idTag,
            MeterStartWh = OcppPayload.Long(payload, "meterStart") ?? 0,
            StartedAt = OcppPayload.Date(payload, "timestamp") ?? now,
            ReceivedStartAt = now,
            IsOpen = true,
            WasRejected = status != OcppAuthorizationStatus.Accepted,
        };
        db.OcppTransactions.Add(tx);
        await db.SaveChanges(cancellationToken);

        return new { transactionId = tx.Id, idTagInfo = new { status } };
    }
}

/// <summary>Stores samples by the charger's timestamp; a replay after an outage is a no-op (R1, R3).</summary>
public sealed class MeterValuesHandler(IApplicationDbContext db, ILogger<MeterValuesHandler> log) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var connectorId = OcppPayload.Int(payload, "connectorId") ?? 0;
        var transactionId = OcppPayload.Int(payload, "transactionId");
        var samples = OcppPayload.Array(payload, "meterValue");
        if (samples is null) return new { };

        transactionId = await MeterValueWriter.ResolveTransactionAsync(db, session, transactionId, log, cancellationToken);
        var rows = MeterValueParser.Parse(samples.Value, session.OcppChargePointId, connectorId, transactionId, DateTime.UtcNow);
        await MeterValueWriter.InsertNewAsync(db, rows, cancellationToken);
        return new { };
    }
}

/// <summary>Closes the session whenever the Stop arrives (R6); an unknown id becomes an orphan row, not a lost message.</summary>
public sealed class StopTransactionHandler(IApplicationDbContext db, ILogger<StopTransactionHandler> log) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var transactionId = OcppPayload.Int(payload, "transactionId");
        var meterStop = OcppPayload.Long(payload, "meterStop") ?? 0;
        var stoppedAt = OcppPayload.Date(payload, "timestamp") ?? now;
        var reason = OcppPayload.Str(payload, "reason");

        var tx = transactionId is null
            ? null
            : await db.OcppTransactions.FirstOrDefaultAsync(
                t => t.Id == transactionId && t.OcppChargePointId == session.OcppChargePointId, cancellationToken);

        if (tx is null)
        {
            log.LogWarning("{ChargePointId}: StopTransaction for unknown transaction {TransactionId} — stored as orphan",
                session.ChargePointId, transactionId);
            tx = new OcppTransaction
            {
                OcppChargePointId = session.OcppChargePointId,
                ConnectorId = 0,
                IdTag = OcppPayload.Str(payload, "idTag") ?? "",
                MeterStartWh = meterStop,
                StartedAt = stoppedAt,
                ReceivedStartAt = now,
                IsOrphan = true,
            };
            db.OcppTransactions.Add(tx);
        }

        tx.MeterStopWh = meterStop;
        tx.StoppedAt = stoppedAt;
        tx.ReceivedStopAt = now;
        tx.StopReason = reason;
        tx.IsOpen = false;
        // A meter that went backwards (replaced / reset) is flagged by a null, never a negative kWh.
        tx.EnergyKwh = !tx.IsOrphan && meterStop >= tx.MeterStartWh ? (meterStop - tx.MeterStartWh) / 1000m : null;
        await db.SaveChanges(cancellationToken);

        // transactionData carries the final samples (context Transaction.End).
        var samples = OcppPayload.Array(payload, "transactionData");
        if (samples is not null)
        {
            var rows = MeterValueParser.Parse(samples.Value, session.OcppChargePointId, tx.ConnectorId, tx.Id, now);
            await MeterValueWriter.InsertNewAsync(db, rows, cancellationToken);
        }

        return new { idTagInfo = new { status = OcppAuthorizationStatus.Accepted } };
    }
}

/// <summary>Vendor-private messages: refused safely, visible in the raw log if a unit turns out to depend on one.</summary>
public sealed class DataTransferHandler : IOcppHandler
{
    public Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken) =>
        Task.FromResult<object>(new { status = "UnknownVendorId" });
}

/// <summary>FirmwareStatusNotification / DiagnosticsStatusNotification: acknowledged, raw-logged, nothing to store yet.</summary>
public sealed class AcknowledgeHandler : IOcppHandler
{
    public Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken) =>
        Task.FromResult<object>(new { });
}

internal static class MeterValueWriter
{
    /// <summary>A transactionId we never issued (lost in an outage) is kept as a connector-level sample rather than failing the FK.</summary>
    public static async Task<int?> ResolveTransactionAsync(IApplicationDbContext db, OcppSession session, int? transactionId, ILogger log, CancellationToken cancellationToken)
    {
        if (transactionId is null) return null;
        var known = await db.OcppTransactions.AsNoTracking()
            .AnyAsync(t => t.Id == transactionId && t.OcppChargePointId == session.OcppChargePointId, cancellationToken);
        if (known) return transactionId;

        log.LogWarning("{ChargePointId}: MeterValues for unknown transaction {TransactionId} — stored without a session",
            session.ChargePointId, transactionId);
        return null;
    }

    /// <summary>Insert only what is not already there (R3). The unique index is the backstop for races.</summary>
    public static async Task InsertNewAsync(IApplicationDbContext db, List<OcppMeterValue> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        var cpId = rows[0].OcppChargePointId;
        var txId = rows[0].OcppTransactionId;
        var times = rows.Select(r => r.MeasuredAt).ToList();
        var existing = await db.OcppMeterValues.AsNoTracking()
            .Where(m => m.OcppChargePointId == cpId && m.OcppTransactionId == txId && times.Contains(m.MeasuredAt))
            .Select(m => m.MeasuredAt)
            .ToListAsync(cancellationToken);
        var seen = existing.ToHashSet();

        var fresh = rows.Where(r => seen.Add(r.MeasuredAt)).ToList();
        if (fresh.Count == 0) return;

        db.OcppMeterValues.AddRange(fresh);
        try
        {
            await db.SaveChanges(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // Lost the race against a parallel replay of the same frame; the rows exist, which is all we wanted.
        }
    }
}
