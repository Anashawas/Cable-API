using Cable.Core.Enums;

namespace Application.ChargingPoints.Queries.GetUpdateRequestById;

/// <summary>
/// Single update request with the field-by-field diff. Old values come from the
/// snapshot taken at submit time, not from the live station.
/// </summary>
public record GetUpdateRequestByIdDto(
    int Id,
    int ChargingPointId,
    string ChargingPointName,
    int RequestedByUserId,
    string? RequestedByUserName,
    string? RequestedByUserPhone,
    RequestStatus RequestStatus,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    int? ReviewedByUserId,
    string? ReviewedByUserName,
    string? RejectionReason,
    List<UpdateRequestChangeDto> Changes,
    List<string> Attachments,
    List<string> RiskFlags,
    List<AttachmentChangeDto> AttachmentChanges
);

public record AttachmentChangeDto(
    int Id,
    AttachmentAction Action,
    string? FileName,
    string? FileUrl,
    int? ExistingAttachmentId
);
