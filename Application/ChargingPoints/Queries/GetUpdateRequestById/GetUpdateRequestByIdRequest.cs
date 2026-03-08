using System.Text.Json;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetUpdateRequestById;

public record GetUpdateRequestByIdRequest(int Id) : IRequest<GetUpdateRequestByIdDto>;

public class GetUpdateRequestByIdRequestHandler(
    IApplicationDbContext context,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetUpdateRequestByIdRequest, GetUpdateRequestByIdDto>
{
    public async Task<GetUpdateRequestByIdDto> Handle(GetUpdateRequestByIdRequest request,
        CancellationToken cancellationToken)
    {
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
                .ThenInclude(cp => cp.ChargingPlugs)
                    .ThenInclude(plug => plug.PlugType)
            .Include(x => x.ChargingPoint.ChargingPointAttachments)
            .Include(x => x.ChargingPoint.ChargerPointType)
            .Include(x => x.ChargingPoint.StationType)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.AttachmentChanges)
                .ThenInclude(ac => ac.ExistingAttachment)
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(ChargingPointUpdateRequest), request.Id);

        var chargingPoint = updateRequest.ChargingPoint;

        // Parse plug type IDs from JSON
        List<int>? newPlugTypeIds = null;
        List<int>? oldPlugTypeIds = null;

        if (!string.IsNullOrEmpty(updateRequest.NewPlugTypeIds))
        {
            newPlugTypeIds = JsonSerializer.Deserialize<List<int>>(updateRequest.NewPlugTypeIds);
        }

        if (!string.IsNullOrEmpty(updateRequest.OldPlugTypeIds))
        {
            oldPlugTypeIds = JsonSerializer.Deserialize<List<int>>(updateRequest.OldPlugTypeIds);
        }

        // Get plug type details for new plug types
        List<PlugTypeSummary>? newPlugTypes = null;
        if (newPlugTypeIds != null && newPlugTypeIds.Any())
        {
            newPlugTypes = await context.PlugTypes
                .Where(pt => newPlugTypeIds.Contains(pt.Id))
                .Select(pt => new PlugTypeSummary(pt.Id, pt.Name, pt.SerialNumber ?? ""))
                .ToListAsync(cancellationToken);
        }

        // Get charger point type name and station type name if changed
        string? newChargerPointTypeName = null;
        if (updateRequest.ChargerPointTypeId.HasValue)
        {
            newChargerPointTypeName = await context.ChargingPointTypes
                .Where(x => x.Id == updateRequest.ChargerPointTypeId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? newStationTypeName = null;
        if (updateRequest.StationTypeId.HasValue)
        {
            newStationTypeName = await context.StationTypes
                .Where(x => x.Id == updateRequest.StationTypeId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Build attachment changes
        var attachmentChanges = updateRequest.AttachmentChanges.Select(ac => new AttachmentChangeDto(
            ac.Id,
            ac.AttachmentAction,
            ac.FileName,
            ac.AttachmentAction == Cable.Core.Enums.AttachmentAction.Add && !string.IsNullOrEmpty(ac.FileName)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, ac.FileName)
                : null,
            ac.ExistingAttachmentId
        )).ToList();

        // Build changes
        var changes = new ChargingPointChanges(
            updateRequest.Name,
            updateRequest.Note,
            updateRequest.CountryName,
            updateRequest.CityName,
            updateRequest.Phone,
            updateRequest.MethodPayment,
            updateRequest.Price,
            updateRequest.FromTime,
            updateRequest.ToTime,
            updateRequest.ChargerSpeed,
            updateRequest.ChargersCount,
            updateRequest.Latitude,
            updateRequest.Longitude,
            updateRequest.ChargerPointTypeId,
            newChargerPointTypeName,
            updateRequest.StationTypeId,
            newStationTypeName,
            updateRequest.OwnerPhone,
            updateRequest.HasOffer,
            updateRequest.Service,
            updateRequest.OfferDescription,
            updateRequest.Address,
            newPlugTypeIds,
            newPlugTypes,
            !string.IsNullOrEmpty(updateRequest.NewIcon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, updateRequest.NewIcon)
                : null,
            !string.IsNullOrEmpty(updateRequest.OldIcon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, updateRequest.OldIcon)
                : null,
            attachmentChanges
        );

        // Build current values
        var currentPlugTypes = chargingPoint.ChargingPlugs
            .Select(cp => new PlugTypeSummary(cp.PlugType.Id, cp.PlugType.Name, cp.PlugType.SerialNumber ?? ""))
            .ToList();

        var currentAttachments = chargingPoint.ChargingPointAttachments
            .Where(a => !a.IsDeleted)
            .Select(a => uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, a.FileName))
            .ToList();

        var currentValues = new ChargingPointCurrentValues(
            chargingPoint.Name,
            chargingPoint.Note,
            chargingPoint.CountryName,
            chargingPoint.CityName,
            chargingPoint.Phone,
            chargingPoint.MethodPayment,
            chargingPoint.Price,
            chargingPoint.FromTime,
            chargingPoint.ToTime,
            chargingPoint.ChargerSpeed,
            chargingPoint.ChargersCount,
            chargingPoint.Latitude,
            chargingPoint.Longitude,
            chargingPoint.ChargerPointTypeId,
            chargingPoint.ChargerPointType?.Name,
            chargingPoint.StationTypeId,
            chargingPoint.StationType?.Name,
            chargingPoint.OwnerPhone,
            chargingPoint.HasOffer,
            chargingPoint.Service,
            chargingPoint.OfferDescription,
            chargingPoint.Address,
            currentPlugTypes,
            !string.IsNullOrEmpty(chargingPoint.Icon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, chargingPoint.Icon)
                : null,
            currentAttachments
        );

        return new GetUpdateRequestByIdDto(
            updateRequest.Id,
            updateRequest.ChargingPointId,
            chargingPoint.Name,
            updateRequest.RequestedByUserId,
            updateRequest.RequestedBy?.Name,
            updateRequest.RequestedBy?.Phone,
            updateRequest.RequestStatus,
            updateRequest.CreatedAt,
            updateRequest.ReviewedAt,
            updateRequest.ReviewedByUserId,
            updateRequest.ReviewedBy?.Name,
            updateRequest.RejectionReason,
            changes,
            currentValues
        );
    }
}
