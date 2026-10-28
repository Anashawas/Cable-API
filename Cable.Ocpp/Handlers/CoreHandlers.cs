using Application.Ocpp;
using Application.Ocpp.Live;
using Application.Pricing;
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
public sealed class BootNotificationHandler(IApplicationDbContext db, Hangfire.IBackgroundJobClient jobs, CommandConfirmer confirmer, LiveEventPublisher live, ILogger<BootNotificationHandler> log) : IOcppHandler
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
        live.Publish(new OcppLiveEvent(OcppLiveEventTypes.Charger, session.ChargingPointId, session.OcppChargePointId, null, null, "booted", now));

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

        // A boot is the proof a Reset (or a TriggerMessage BootNotification) took effect.
        await confirmer.ConfirmAsync(cp.Id, "Reset", _ => true, cancellationToken);
        await confirmer.ConfirmAsync(cp.Id, "TriggerMessage", r => CommandConfirmer.Str(r, "requestedMessage") == "BootNotification", cancellationToken);

        // currentTime sets the charger's clock — real UTC, no local conversion (R7).
        return new { status = "Accepted", currentTime = now, interval = cp.HeartbeatInterval };
    }
}

public sealed class HeartbeatHandler(CommandConfirmer confirmer) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        await confirmer.ConfirmAsync(session.OcppChargePointId, "TriggerMessage", r => CommandConfirmer.Str(r, "requestedMessage") == "Heartbeat", cancellationToken);
        return new { currentTime = DateTime.UtcNow };
    }
}

/// <summary>Upserts the connector row; unknown connector ids are created, never refused. Faulted → owner notification.</summary>
public sealed class StatusNotificationHandler(IApplicationDbContext db, FaultNotifier faults, CommandConfirmer confirmer, LiveEventPublisher live) : IOcppHandler
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
        live.Publish(new OcppLiveEvent(OcppLiveEventTypes.Connector, session.ChargingPointId, session.OcppChargePointId, connectorId, null, status, now));

        if (status == OcppConnectorStatus.Faulted)
            faults.Report(connector.Id, session.ChargePointId, connectorId, errorCode, info);

        // Confirmations: the plug reporting the state we asked for.
        await confirmer.ConfirmAsync(session.OcppChargePointId, "ChangeAvailability", r =>
        {
            var target = CommandConfirmer.Int(r, "connectorId") ?? -1;
            if (target != 0 && target != connectorId) return false;
            var inoperative = CommandConfirmer.Str(r, "type") == "Inoperative";
            return inoperative ? status == OcppConnectorStatus.Unavailable : status != OcppConnectorStatus.Unavailable;
        }, cancellationToken);
        await confirmer.ConfirmAsync(session.OcppChargePointId, "UnlockConnector",
            r => CommandConfirmer.Int(r, "connectorId") == connectorId && status is OcppConnectorStatus.Available or OcppConnectorStatus.Finishing, cancellationToken);
        await confirmer.ConfirmAsync(session.OcppChargePointId, "TriggerMessage", r =>
            CommandConfirmer.Str(r, "requestedMessage") == "StatusNotification"
            && (CommandConfirmer.Int(r, "connectorId") is null or 0 || CommandConfirmer.Int(r, "connectorId") == connectorId), cancellationToken);

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
public sealed class StartTransactionHandler(IApplicationDbContext db, TagAuthorizer authorizer, CommandConfirmer confirmer, LiveEventPublisher live, DriverSessionNotifier drivers) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var idTag = OcppPayload.Str(payload, "idTag") ?? "";
        var status = await authorizer.AuthorizeAsync(session, idTag, cancellationToken);
        var tag = OcppCredentials.NormalizeIdTag(idTag);

        // Who started it: the app (virtual user tag), an operator (virtual station tag → the admin / owner
        // who sent the RemoteStart), or a card — linked to a driver when OcppUserIdTag says so.
        var source = OcppVirtualTag.SourceOf(tag);
        int? startedBy = OcppVirtualTag.UserIdOf(tag);
        if (startedBy is null && source == OcppStartSource.Operator)
        {
            var since = now - OcppVirtualTag.RemoteStartAuthorizeWindow;
            var needle = "\"idTag\":\"" + tag + "\"";
            startedBy = await db.OcppCommands.AsNoTracking()
                .Where(c => c.OcppChargePointId == session.OcppChargePointId && c.Action == "RemoteStartTransaction" && !c.IsDeleted
                            && c.CreatedAt >= since && c.RequestPayload.Contains(needle))
                .OrderByDescending(c => c.Id).Select(c => c.CreatedBy).FirstOrDefaultAsync(cancellationToken);
        }
        else if (startedBy is null)
        {
            startedBy = await db.OcppUserIdTags.AsNoTracking()
                .Where(u => u.IdTag == tag && u.IsEnabled && !u.IsDeleted)
                .Select(u => (int?)u.UserId).FirstOrDefaultAsync(cancellationToken);
        }

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
            StartSource = source,
            StartedByUserId = startedBy,
        };

        // A limit chosen with the remote start (app / partner / admin) waits on the plug: this session takes it.
        if (!tx.WasRejected && source != OcppStartSource.Card)
        {
            var since = now - OcppVirtualTag.RemoteStartAuthorizeWindow;
            var plug = await db.OcppConnectors.FirstOrDefaultAsync(c => c.OcppChargePointId == session.OcppChargePointId
                && c.ConnectorId == tx.ConnectorId && c.PendingLimitKind != null && c.PendingLimitSetAt >= since, cancellationToken);
            if (plug is not null)
            {
                tx.ChargeLimitKind = plug.PendingLimitKind;
                tx.ChargeLimitValue = plug.PendingLimitValue;
                tx.ChargeLimitSetBy = plug.PendingLimitSetBy;
                tx.ChargeLimitSetAt = now;
                plug.PendingLimitKind = null;
                plug.PendingLimitValue = null;
                plug.PendingLimitSetBy = null;
                plug.PendingLimitSetAt = null;
            }
        }

        // Sprint 5 (A): the app start's position / distance and car, parked on the plug for this driver's session.
        await Application.Ocpp.LiveCharging.OcppPendingStart.ApplyAsync(db, session.OcppChargePointId, tx, now, cancellationToken);

        db.OcppTransactions.Add(tx);
        await db.SaveChanges(cancellationToken);
        live.Publish(new OcppLiveEvent(OcppLiveEventTypes.Session, session.ChargingPointId, session.OcppChargePointId, tx.ConnectorId, tx.Id, "started", now));
        drivers.Started(tx, now);

        // A RemoteStartTransaction we sent is confirmed by the session actually opening.
        if (!tx.WasRejected && source != OcppStartSource.Card)
            await confirmer.ConfirmAsync(session.OcppChargePointId, "RemoteStartTransaction", r =>
                string.Equals(CommandConfirmer.Str(r, "idTag"), tag, StringComparison.OrdinalIgnoreCase)
                && (CommandConfirmer.Int(r, "connectorId") is null || CommandConfirmer.Int(r, "connectorId") == tx.ConnectorId), cancellationToken);

        return new { transactionId = tx.Id, idTagInfo = new { status } };
    }
}

/// <summary>Stores samples by the charger's timestamp; a replay after an outage is a no-op (R1, R3).</summary>
public sealed class MeterValuesHandler(IApplicationDbContext db, CommandConfirmer confirmer, ChargeLimitEnforcer limits, LiveEventPublisher live, ILogger<MeterValuesHandler> log) : IOcppHandler
{
    public async Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken)
    {
        var connectorId = OcppPayload.Int(payload, "connectorId") ?? 0;
        var transactionId = OcppPayload.Int(payload, "transactionId");
        var samples = OcppPayload.Array(payload, "meterValue");
        await confirmer.ConfirmAsync(session.OcppChargePointId, "TriggerMessage", r =>
            CommandConfirmer.Str(r, "requestedMessage") == "MeterValues"
            && (CommandConfirmer.Int(r, "connectorId") is null or 0 || CommandConfirmer.Int(r, "connectorId") == connectorId), cancellationToken);
        if (samples is null) return new { };

        transactionId = await MeterValueWriter.ResolveTransactionAsync(db, session, transactionId, log, cancellationToken);
        var rows = MeterValueParser.Parse(samples.Value, session.OcppChargePointId, connectorId, transactionId, DateTime.UtcNow);
        await MeterValueWriter.InsertNewAsync(db, rows, cancellationToken);

        if (transactionId is int txId)
        {
            if (rows.Any(r => r.SocPercent is > 0))
                await MeterValueWriter.UpdateSocAsync(db, txId, cancellationToken);
            await limits.CheckAsync(db, session, txId, cancellationToken);
            live.Publish(new OcppLiveEvent(OcppLiveEventTypes.Session, session.ChargingPointId, session.OcppChargePointId, connectorId, txId, "updated", DateTime.UtcNow));
        }
        return new { };
    }
}

/// <summary>Closes the session whenever the Stop arrives (R6); an unknown id becomes an orphan row, not a lost message.</summary>
public sealed class StopTransactionHandler(IApplicationDbContext db, CommandConfirmer confirmer, LiveEventPublisher live, DriverSessionNotifier drivers, ILogger<StopTransactionHandler> log) : IOcppHandler
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

        // The first Stop is final. hjl 1.0.26.x later re-sends a Stop for a session it still remembers,
        // quoting our id with the meter reading of that moment (Oct 19: #273 closed a day late with the
        // next session's energy). Acknowledge it so the unit stops retrying, change nothing.
        if (tx is { IsOpen: false, IsOrphan: false })
        {
            log.LogWarning("{ChargePointId}: StopTransaction for session {TransactionId} that is already closed (meterStop {MeterStop}, kept {KeptStop}) — ignored",
                session.ChargePointId, tx.Id, meterStop, tx.MeterStopWh);
            return new { idTagInfo = new { status = OcppAuthorizationStatus.Accepted } };
        }

        // Some firmware (hjl 1.0.26.x) replays sessions it ran offline as StartTransaction (we issue an id)
        // followed at once by a StopTransaction that quotes its OWN local id instead of ours. Close the
        // session we just opened for it rather than leaving it open forever and its energy unpriced.
        if (tx is null)
        {
            tx = await FindReplayedStartAsync(session, OcppPayload.Str(payload, "idTag"), stoppedAt, meterStop, now, cancellationToken);
            if (tx is not null)
                log.LogWarning("{ChargePointId}: StopTransaction quotes unknown id {TransactionId} — matched to replayed session {MatchedId} (same idTag, opened {Seconds:0}s ago)",
                    session.ChargePointId, transactionId, tx.Id, (now - tx.ReceivedStartAt).TotalSeconds);
        }

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
        // "Stale" means "open with no Stop for 24 h"; a session that did get its Stop is no longer stale.
        tx.IsStale = false;
        // A meter that went backwards (replaced / reset) is flagged by a null, never a negative kWh.
        tx.EnergyKwh = !tx.IsOrphan && meterStop >= tx.MeterStartWh ? (meterStop - tx.MeterStartWh) / 1000m : null;
        await db.SaveChanges(cancellationToken);

        // A RemoteStopTransaction we sent is confirmed by this very message.
        if (!tx.IsOrphan)
            await confirmer.ConfirmAsync(session.OcppChargePointId, "RemoteStopTransaction", r => CommandConfirmer.Int(r, "transactionId") == tx.Id, cancellationToken);

        // Price it from the time-of-use tariff. Never fails the reply: the job PriceOcppSessions catches up.
        try
        {
            if (await SessionPricingService.PriceAsync(db, tx, null, cancellationToken))
                await db.SaveChanges(cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{ChargePointId}: could not price session {TransactionId} now — left for the pricing job", session.ChargePointId, tx.Id);
        }

        // transactionData carries the final samples (context Transaction.End).
        var samples = OcppPayload.Array(payload, "transactionData");
        if (samples is not null)
        {
            var rows = MeterValueParser.Parse(samples.Value, session.OcppChargePointId, tx.ConnectorId, tx.Id, now);
            await MeterValueWriter.InsertNewAsync(db, rows, cancellationToken);
        }
        if (!tx.IsOrphan)
        {
            await MeterValueWriter.UpdateSocAsync(db, tx.Id, cancellationToken);
            // Sprint 5 (A, 13.4): a car that reported a battery % marks its model ReportsSoc. Never fails the reply.
            try { await Application.Ocpp.LiveCharging.CarSocLearning.MarkAsync(db, tx.Id, cancellationToken); }
            catch (Exception ex) { log.LogWarning(ex, "{ChargePointId}: could not record battery-% support for session {TransactionId}", session.ChargePointId, tx.Id); }
            live.Publish(new OcppLiveEvent(OcppLiveEventTypes.Session, session.ChargingPointId, session.OcppChargePointId, tx.ConnectorId, tx.Id, "stopped", now));
            drivers.Completed(tx, now);
        }

        return new { idTagInfo = new { status = OcppAuthorizationStatus.Accepted } };
    }

    /// <summary>How recently the replayed StartTransaction must have reached us: replays arrive back to back.</summary>
    internal static readonly TimeSpan ReplayMatchWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The open session on this charger that a Stop with an unknown id most likely belongs to: same idTag
    /// (when the Stop carries one), started by the charger's clock no later than the stop, meter not
    /// running backwards, and received by us within <see cref="ReplayMatchWindow"/> — the latest such start.
    /// Live sessions opened earlier than the window are never touched.
    /// </summary>
    private async Task<OcppTransaction?> FindReplayedStartAsync(OcppSession session, string? stopIdTag, DateTime stoppedAt,
        long meterStop, DateTime now, CancellationToken cancellationToken)
    {
        var receivedSince = now - ReplayMatchWindow;
        var candidates = await db.OcppTransactions
            .Where(t => t.OcppChargePointId == session.OcppChargePointId && t.IsOpen && !t.IsOrphan
                        && t.ReceivedStartAt >= receivedSince && t.StartedAt <= stoppedAt && t.MeterStartWh <= meterStop)
            .OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

        // Without an idTag the Stop could belong to either plug: only an unambiguous single candidate is taken.
        if (string.IsNullOrWhiteSpace(stopIdTag))
            return candidates.Count == 1 ? candidates[0] : null;

        var tag = OcppCredentials.NormalizeIdTag(stopIdTag);
        return candidates.FirstOrDefault(t => OcppCredentials.NormalizeIdTag(t.IdTag) == tag);
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

    /// <summary>Battery % at the first and latest reading that carried one (readings may arrive out of order after an outage).</summary>
    public static async Task UpdateSocAsync(IApplicationDbContext db, int transactionId, CancellationToken cancellationToken)
    {
        var socs = db.OcppMeterValues.AsNoTracking().Where(m => m.OcppTransactionId == transactionId && m.SocPercent > 0);
        var first = await socs.OrderBy(m => m.MeasuredAt).ThenBy(m => m.Id).Select(m => m.SocPercent).FirstOrDefaultAsync(cancellationToken);
        if (first is null) return;
        var last = await socs.OrderByDescending(m => m.MeasuredAt).ThenByDescending(m => m.Id).Select(m => m.SocPercent).FirstOrDefaultAsync(cancellationToken);
        await db.OcppTransactions.Where(t => t.Id == transactionId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SocStartPercent, first).SetProperty(t => t.SocEndPercent, last), cancellationToken);
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
