using Domain.Common;

namespace Domain.Enitites;

public class ServiceProviderAttachment : BaseAuditableEntity
{
    public int ServiceProviderId { get; set; }
    public long FileSize { get; set; }
    public string FileExtension { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;

    public virtual ServiceProvider ServiceProvider { get; set; } = null!;
}
