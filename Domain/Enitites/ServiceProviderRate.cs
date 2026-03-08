using Domain.Common;

namespace Domain.Enitites;

public class ServiceProviderRate : BaseAuditableEntity
{
    public int ServiceProviderId { get; set; }
    public int UserId { get; set; }
    public int Rating { get; set; }
    public double AVGRating { get; set; }
    public string? Comment { get; set; }

    public virtual ServiceProvider ServiceProvider { get; set; } = null!;
    public virtual UserAccount User { get; set; } = null!;
}
