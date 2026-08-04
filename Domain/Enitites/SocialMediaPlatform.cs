using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Catalog of social-media platforms (Facebook, Instagram, ...). Each platform has an
/// optional icon image uploaded to the <c>CableSocialMediaIcons</c> folder and reused
/// by every provider/station link that points at this platform.
/// </summary>
public class SocialMediaPlatform : BaseAuditableEntity
{
    public string  Name            { get; set; } = null!;
    public string? NameAr          { get; set; }
    public string? IconFileName    { get; set; }
    public string? IconExtension   { get; set; }
    public string? IconContentType { get; set; }
    public long?   IconFileSize    { get; set; }
    public int     DisplayOrder    { get; set; }
    public bool    IsActive        { get; set; } = true;

    public virtual ICollection<SocialLink> Links { get; set; } = new List<SocialLink>();
}
