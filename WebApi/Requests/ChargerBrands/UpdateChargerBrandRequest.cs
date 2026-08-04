namespace Cable.Requests.ChargerBrands;

/// <summary>
/// Request to rename a charger brand.
/// </summary>
/// <param name="Name">The new brand name</param>
public record UpdateChargerBrandRequest(string Name);
