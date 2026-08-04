namespace Domain.Enitites;

/// <summary>
/// Lookup table for charger brands (replaces the free-text ChargingPoint.ChargerBrand string).
/// </summary>
public class ChargerBrand
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<ChargingPointChargerBrand> ChargingPointBrands { get; set; } =
        new List<ChargingPointChargerBrand>();
}
