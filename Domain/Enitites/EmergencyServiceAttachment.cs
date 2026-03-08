using Domain.Common;

namespace Domain.Enitites;

public partial class EmergencyServiceAttachment : BaseAuditableEntity
{
    public int EmergencyServiceId { get; set; }
    public long FileSize { get; set; }
    public string FileExtension { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;

    public virtual EmergencyService EmergencyService { get; set; } = null!;
}
