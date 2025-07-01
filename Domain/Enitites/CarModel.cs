namespace Domain.Enitites;

public class CarModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int CarTypeId { get; set; }
    public CarType CarType { get; set; } = new();
    public ICollection<Car>  Cars { get; set; } = new List<Car>();
}
