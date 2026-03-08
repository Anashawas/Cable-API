using Application.ChargingPoints.Queries.GetChargingPointById;
using Cable.Core.Enums;

namespace Application.ChargingPoints.Queries.GetUpdateRequestById;

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
    ChargingPointChanges Changes,
    ChargingPointCurrentValues CurrentValues
);

public record ChargingPointChanges(
    string? Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double? Latitude,
    double? Longitude,
    int? ChargerPointTypeId,
    string? ChargerPointTypeName,
    int? StationTypeId,
    string? StationTypeName,
    string? OwnerPhone,
    bool? HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<int>? PlugTypeIds,
    List<PlugTypeSummary>? PlugTypes,
    string? NewIcon,
    string? OldIcon,
    List<AttachmentChangeDto>? AttachmentChanges
);

public record ChargingPointCurrentValues(
    string Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double Latitude,
    double Longitude,
    int ChargerPointTypeId,
    string? ChargerPointTypeName,
    int StationTypeId,
    string? StationTypeName,
    string? OwnerPhone,
    bool HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<PlugTypeSummary> PlugTypes,
    string? Icon,
    List<string> Attachments
);

public record AttachmentChangeDto(
    int Id,
    AttachmentAction Action,
    string? FileName,
    string? FileUrl,
    int? ExistingAttachmentId
);
