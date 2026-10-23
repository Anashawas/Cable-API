using System.Text.Json;
using Application.Common.Interfaces;
using Application.Ocpp;
using Application.Ocpp.Live;
using Application.Pricing;
using Cable.Ocpp.Transport;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Cable.Ocpp.Services;

/// <summary>
/// Checks a session against its charge limit (JOD / kWh / battery %) on every reading and, when it is
/// reached, sends RemoteStopTransaction itself — OCPP 1.6 units cannot hold such a limit. The stop is sent
/// off the charger's message loop (the reply arrives on that same socket) and audited as an OcppCommand.
/// If the unit does not accept it, ChargeLimitReachedAt is cleared so the next reading tries again.
/// </summary>
public sealed class ChargeLimitEnforcer(IServiceScopeFactory scopes, CommandSender sender, LiveEventPublisher live,
    ILogger<ChargeLimitEnforcer> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task CheckAsync(IApplicationDbContext db, OcppSession session, int transactionId, CancellationToken cancellationToken)
    {
        var tx = await db.OcppTransactions
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.OcppChargePointId == session.OcppChargePointId
                                      && t.IsOpen && !t.WasRejected && !t.IsOrphan
                                      && t.ChargeLimitKind != null && t.ChargeLimitValue != null && t.ChargeLimitReachedAt == null,
                cancellationToken);
        if (tx is null) return;

        var samples = await db.OcppMeterValues.AsNoTracking()
            .Where(m => m.OcppTransactionId == tx.Id)
            .OrderBy(m => m.MeasuredAt)
            .Select(m => new { m.MeasuredAt, m.EnergyWh, m.PowerW, m.SocPercent })
            .ToListAsync(cancellationToken);
        if (samples.Count == 0) return;

        var now = DateTime.UtcNow;
        var withEnergy = samples.Where(s => s.EnergyWh != null && s.EnergyWh >= tx.MeterStartWh).ToList();
        var latestWh = withEnergy.Count > 0 ? withEnergy[^1].EnergyWh!.Value : tx.MeterStartWh;
        var energyKwh = (latestWh - tx.MeterStartWh) / 1000m;
        var power = samples.LastOrDefault(s => s.PowerW != null)?.PowerW;
        var soc = samples.LastOrDefault(s => s.SocPercent is > 0)?.SocPercent;
        var sampleSeconds = samples.Count >= 2
            ? (int)Math.Clamp((samples[^1].MeasuredAt - samples[^2].MeasuredAt).TotalSeconds, 5, 120)
            : OcppChargeLimit.DefaultSampleSeconds;

        var costFils = 0;
        var rateFils = 0;
        if (tx.ChargeLimitKind == OcppChargeLimit.Jod)
        {
            var tariff = await TouTariffLoader.LoadAsync(db, cancellationToken);
            if (tariff is null) return;   // no tariff = no price = cannot judge a JOD limit
            costFils = SessionPricer.Compute(tariff, tx.StartedAt, now, tx.MeterStartWh, latestWh,
                withEnergy.Select(s => (s.MeasuredAt, s.EnergyWh!.Value))).CostFils;
            rateFils = SessionPricer.WindowNow(tariff, now)?.PriceFils ?? 0;
        }

        if (!OcppChargeLimit.ShouldStop(tx.ChargeLimitKind!, tx.ChargeLimitValue!.Value, energyKwh, costFils, soc, power, sampleSeconds, rateFils))
            return;

        tx.ChargeLimitReachedAt = now;
        await db.SaveChanges(cancellationToken);
        log.LogInformation("{ChargePointId}: session {TransactionId} reached its limit {Limit} ({Kwh} kWh, {Fils} fils, {Soc}%) — stopping",
            session.ChargePointId, tx.Id, OcppChargeLimit.Describe(tx.ChargeLimitKind, tx.ChargeLimitValue), energyKwh, costFils, soc);
        live.Publish(new OcppLiveEvent(OcppLiveEventTypes.Session, session.ChargingPointId, session.OcppChargePointId,
            tx.ConnectorId, tx.Id, "limit-reached", now));

        var chargePointId = session.ChargePointId;
        var ocppChargePointId = session.OcppChargePointId;
        var txId = tx.Id;
        _ = Task.Run(() => SendStopAsync(chargePointId, ocppChargePointId, txId));
    }

    private async Task SendStopAsync(string chargePointId, int ocppChargePointId, int transactionId)
    {
        try
        {
            var payload = new Dictionary<string, object> { ["transactionId"] = transactionId };
            var outcome = await sender.SendAsync(chargePointId, "RemoteStopTransaction",
                JsonSerializer.SerializeToElement(payload, Json), CommandSender.DefaultTimeout, CancellationToken.None);
            var result = ReadStatus(outcome.Payload);
            var accepted = outcome.Status == "Answered" && result == "Accepted";

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.OcppCommands.Add(new OcppCommand
            {
                OcppChargePointId = ocppChargePointId,
                Action = "RemoteStopTransaction",
                RequestPayload = JsonSerializer.Serialize(payload, Json),
                Status = outcome.Status,
                ResultStatus = result,
                ResponsePayload = outcome.Payload,
                ErrorCode = outcome.ErrorCode,
                ErrorDescription = accepted ? "Charge limit reached" : $"Charge limit reached — stop not accepted: {outcome.ErrorDescription ?? result}",
                DurationMs = (int)Math.Min(outcome.ElapsedMs, int.MaxValue),
            });
            if (!accepted)
            {
                // Try again on the next reading.
                await db.OcppTransactions.Where(t => t.Id == transactionId && t.IsOpen)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ChargeLimitReachedAt, (DateTime?)null));
                log.LogWarning("{ChargePointId}: limit stop for session {TransactionId} not accepted ({Status} {Result}) — will retry on the next reading",
                    chargePointId, transactionId, outcome.Status, result);
            }
            await db.SaveChanges(CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "{ChargePointId}: limit stop for session {TransactionId} failed", chargePointId, transactionId);
        }
    }

    private static string? ReadStatus(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
