using Domain.Common;

namespace Domain.Enitites;

public partial class BannerDuration : BaseAuditableEntity
{
    public int BannerId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public Banner Banner { get; set; }
    
}