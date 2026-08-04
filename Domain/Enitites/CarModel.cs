namespace Domain.Enitites;

public class CarModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int CarTypeId { get; set; }

    /// <summary>FK to the CarModelSize lookup (SUV, Hatchback, Sedan, ...).</summary>
    public int? SizeId { get; set; }

    public CarType CarType { get; set; }  =null!;
    public CarModelSize? Size { get; set; }
    public virtual ICollection<UserCar> UserCars { get; set; } = new List<UserCar>();

}
