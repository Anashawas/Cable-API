using Domain.Common;

namespace Domain.Enitites;

public class ServiceCategory : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<ServiceProvider> ServiceProviders { get; set; } = new List<ServiceProvider>();
}
