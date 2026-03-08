using Cable.Core.Enums;

namespace Application.ChargingPoints.Queries.GetPendingUpdateRequests;

public record GetPendingUpdateRequestsDto(
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
    string? RejectionReason
);
