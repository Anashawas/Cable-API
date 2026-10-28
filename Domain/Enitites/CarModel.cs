namespace Domain.Enitites;

public class CarModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int CarTypeId { get; set; }

    /// <summary>FK to the CarModelSize lookup (SUV, Hatchback, Sedan, ...).</summary>
    public int? SizeId { get; set; }

    /// <summary>
    /// Learned from Cable Connect sessions: true once a session of a car of this model reported a battery %
    /// (SoC). Null = not seen yet. The app hides the "stop at battery %" limit for cars that do not report it.
    /// </summary>
    public bool? ReportsSoc { get; set; }

    public CarType CarType { get; set; }  =null!;
    public CarModelSize? Size { get; set; }
    public virtual ICollection<UserCar> UserCars { get; set; } = new List<UserCar>();

}
