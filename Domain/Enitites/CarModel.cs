namespace Domain.Enitites;

public class CarModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int CarTypeId { get; set; }
    public CarType CarType { get; set; }  =null!;
    public virtual ICollection<UserCar> UserCars { get; set; } = new List<UserCar>();

}
