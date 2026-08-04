#nullable enable
using Domain.Common;

namespace Domain.Enitites;

public partial class OfferAttachment : BaseAuditableEntity
{
    public int OfferId { get; set; }

    public long FileSize { get; set; }

    public string FileExtension { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public virtual ProviderOffer Offer { get; set; } = null!;
}
