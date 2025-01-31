using Domain.Common;

namespace Domain.Enitites;

public partial class BannerAttachment : BaseAuditableEntity
{
    public int BannerId { get; set; }
    public string ContentType { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; } 
    public string FileExtension { get; set; } = null!;

    public Banner Banner { get; set; } 

}