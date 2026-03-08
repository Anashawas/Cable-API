using Cable.Core.Enums;
using Domain.Common;

namespace Domain.Enitites;

public class ChargingPointUpdateRequestAttachment : BaseAuditableEntity
{
    public int UpdateRequestId { get; set; }
    public AttachmentAction AttachmentAction { get; set; }

    // For new attachments (Action = Add)
    public string? FileName { get; set; }
    public string? FileExtension { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }

    // For deletion requests (Action = Delete)
    public int? ExistingAttachmentId { get; set; }

    // Navigation properties
    public virtual ChargingPointUpdateRequest UpdateRequest { get; set; } = null!;
    public virtual ChargingPointAttachment? ExistingAttachment { get; set; }
}
