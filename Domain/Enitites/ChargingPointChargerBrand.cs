using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Junction: which charger brands a charging point has, and how many chargers
/// of each brand (e.g. 4× ABB + 3× Teison at one station). One row per
/// (station, brand); rows are hard-replaced when the station's brands are set.
/// </summary>
public class ChargingPointChargerBrand : BaseEntity
{
    public int ChargingPointId { get; set; }
    public int ChargerBrandId { get; set; }

    /// <summary>Number of chargers of this brand at the station.</summary>
    public int Count { get; set; }

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
    public virtual ChargerBrand ChargerBrand { get; set; } = null!;
}
