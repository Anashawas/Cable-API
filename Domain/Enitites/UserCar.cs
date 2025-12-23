using Domain.Common;

namespace Domain.Enitites;

public partial class UserCar : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int CarModelId { get; set; }
    public int PlugTypeId { get; set; }
    public UserAccount UserAccount { get; set; }  
    public PlugType PlugType { get; set; }  
    public CarModel CarModel { get; set; }

}