namespace Domain.Enitites;

/// <summary>
/// One sampled reading: everything the charger reported for a single timestamp,
/// flattened to columns. The pilot unit samples every 15 s per connector, so this
/// table grows fastest — hence a bigint key and no audit columns.
/// Unique on (charge point, transaction, measuredAt): a replay after an outage
/// must not double the energy (R3).
/// </summary>
public class OcppMeterValue
{
    public long Id { get; set; }

    public int OcppChargePointId { get; set; }
    public int ConnectorId { get; set; }
    public int? OcppTransactionId { get; set; }

    /// <summary>The charger's sample timestamp (R1).</summary>
    public DateTime MeasuredAt { get; set; }
    public DateTime ReceivedAt { get; set; }

    /// <summary>Sample.Periodic, Sample.Clock, Transaction.Begin, Transaction.End, …</summary>
    public string? Context { get; set; }

    /// <summary>Energy.Active.Import.Register — cumulative, in Wh. Session energy = this − MeterStartWh.</summary>
    public long? EnergyWh { get; set; }
    public int? PowerW { get; set; }
    public decimal? CurrentA { get; set; }
    public decimal? VoltageV { get; set; }

    /// <summary>Only when the car reports it; the UI must cope with null.</summary>
    public int? SocPercent { get; set; }
    public decimal? TemperatureC { get; set; }

    public virtual OcppChargePoint ChargePoint { get; set; } = null!;
    public virtual OcppTransaction? Transaction { get; set; }
}
