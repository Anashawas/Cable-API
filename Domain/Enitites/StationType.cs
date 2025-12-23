namespace Domain.Enitites;

public class StationType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<ChargingPoint> ChargingPoints { get; set; }
}