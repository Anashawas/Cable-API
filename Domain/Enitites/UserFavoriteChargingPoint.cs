using Domain.Common;

namespace Domain.Enitites;

public partial class UserFavoriteChargingPoint : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int ChargingPointId { get; set; }

    public virtual UserAccount User { get; set; } = null!;
    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
}
