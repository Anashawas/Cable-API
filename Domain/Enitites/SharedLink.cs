using Domain.Common;

namespace Domain.Enitites;

public partial class SharedLink : BaseAuditableEntity
{
    public string LinkToken { get; set; } = null!;
    public string LinkType { get; set; } = null!;
    public int? TargetId { get; set; }
    public string? Parameters { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int MaxUsage { get; set; } = 1;
    public int CurrentUsage { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public virtual ICollection<SharedLinkUsage> SharedLinkUsages { get; set; } = new List<SharedLinkUsage>();
}