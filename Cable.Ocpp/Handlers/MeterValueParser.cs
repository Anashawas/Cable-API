using System.Globalization;
using System.Text.Json;
using Domain.Enitites;

namespace Cable.Ocpp.Handlers;

/// <summary>
/// Flattens OCPP <c>meterValue[] → sampledValue[]</c> into one <see cref="OcppMeterValue"/>
/// per timestamp. Phase-specific samples (L1/L2/L3) and signed-data blobs are skipped;
/// units are normalised to Wh / W / A / V / % / °C.
/// </summary>
public static class MeterValueParser
{
    public static List<OcppMeterValue> Parse(JsonElement meterValues, int ocppChargePointId, int connectorId, int? transactionId, DateTime receivedAt)
    {
        var rows = new List<OcppMeterValue>();
        if (meterValues.ValueKind != JsonValueKind.Array) return rows;

        foreach (var mv in meterValues.EnumerateArray())
        {
            var measuredAt = OcppPayload.Date(mv, "timestamp");
            if (measuredAt is null) continue;

            var row = new OcppMeterValue
            {
                OcppChargePointId = ocppChargePointId,
                ConnectorId = connectorId,
                OcppTransactionId = transactionId,
                MeasuredAt = measuredAt.Value,
                ReceivedAt = receivedAt,
            };

            var samples = OcppPayload.Array(mv, "sampledValue");
            if (samples is null) continue;

            foreach (var s in samples.Value.EnumerateArray())
            {
                if (OcppPayload.Str(s, "phase") is not null) continue;            // per-phase detail, not the total
                if (OcppPayload.Str(s, "format") == "SignedData") continue;       // opaque OCMF blob

                var raw = OcppPayload.Str(s, "value");
                if (raw is null || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    continue;

                row.Context ??= OcppPayload.Str(s, "context");
                var unit = OcppPayload.Str(s, "unit");

                // Spec default when measurand is absent (BE_OCPP_16_REQUIREMENTS §6.6).
                switch (OcppPayload.Str(s, "measurand") ?? "Energy.Active.Import.Register")
                {
                    case "Energy.Active.Import.Register":
                        row.EnergyWh = (long)Math.Round(unit is "kWh" ? value * 1000 : value);
                        break;
                    case "Power.Active.Import":
                        row.PowerW = (int)Math.Round(unit is "kW" ? value * 1000 : value);
                        break;
                    case "Current.Import":
                        row.CurrentA = Math.Round(value, 2);
                        break;
                    case "Voltage":
                        row.VoltageV = Math.Round(value, 2);
                        break;
                    case "SoC":
                        row.SocPercent = (int)Math.Round(value);
                        break;
                    case "Temperature":
                        row.TemperatureC = unit switch
                        {
                            "Fahrenheit" => Math.Round((value - 32) * 5 / 9, 1),
                            "K" => Math.Round(value - 273.15m, 1),
                            _ => Math.Round(value, 1),
                        };
                        break;
                    // Energy.Active.Import.Interval, Power.Offered, Frequency, RPM … are not stored in phase 1.
                }
            }

            rows.Add(row);
        }

        return rows;
    }
}
