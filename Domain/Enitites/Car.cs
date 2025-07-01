namespace Domain.Enitites;

public class Car
{
    public int Id { get; set; }
    public int CarModelId { get; set; }
    public CarModel CarModel { get; set; } = new();
    public ICollection<UserCar>  UserCars { get; set; } =  new List<UserCar>();
}