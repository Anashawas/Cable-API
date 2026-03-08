using Domain.Common;

namespace Domain.Enitites;

public class UserFavoriteServiceProvider : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int ServiceProviderId { get; set; }

    public virtual UserAccount User { get; set; } = null!;
    public virtual ServiceProvider ServiceProvider { get; set; } = null!;
}
