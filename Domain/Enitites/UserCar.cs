using Domain.Common;

namespace Domain.Enitites;

public partial class UserCar : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int CarId { get; set; }
    public int PlugTypeId { get; set; }
    public UserAccount UserAccount { get; set; } = new();
    public Car Car { get; set; } = new();
    public PlugType PlugType { get; set; } = new();

}