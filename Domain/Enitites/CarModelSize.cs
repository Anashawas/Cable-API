namespace Domain.Enitites;

/// <summary>
/// Lookup table for car model sizes / body types (SUV, Hatchback, Sedan, ...).
/// </summary>
public class CarModelSize
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<CarModel> CarModels { get; set; } = new List<CarModel>();
}
