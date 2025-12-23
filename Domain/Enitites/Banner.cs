using System.Security.AccessControl;
using Domain.Common;

namespace Domain.Enitites;

public partial class Banner : BaseAuditableEntity
{

    public string Name { get; set; } = null!;

    public string Phone { get; set; } = null!;
    public string Email { get; set; } = null!;
    public int? ActionType { get; set; }
    public string? ActionUrl { get; set; }
    public ICollection<BannerDuration> BannerDurations { get; set; } = (List<BannerDuration>) [];
    public ICollection<BannerAttachment> BannerAttachments { get; set; } = (List<BannerAttachment>) [];

}