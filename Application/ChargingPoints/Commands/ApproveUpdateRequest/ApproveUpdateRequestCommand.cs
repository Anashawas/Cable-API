using System.Text.Json;
using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.ApproveUpdateRequest;

public record ApproveUpdateRequestCommand(int UpdateRequestId) : IRequest;

public class ApproveUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<ApproveUpdateRequestCommand>
{
    public async Task Handle(ApproveUpdateRequestCommand request, CancellationToken cancellationToken)
    {
        // 1. Get the update request with all related data
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
                .ThenInclude(cp => cp.ChargingPlugs)
            .Include(x => x.ChargingPoint.ChargingPointAttachments)
            .Include(x => x.AttachmentChanges)
                .ThenInclude(ac => ac.ExistingAttachment)
            .FirstOrDefaultAsync(x => x.Id == request.UpdateRequestId && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(ChargingPointUpdateRequest), request.UpdateRequestId);

        // 2. Verify status is pending
        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("UpdateRequestId", "Only pending requests can be approved");

        var chargingPoint = updateRequest.ChargingPoint;

        // 3. Apply basic field changes
        if (updateRequest.Name != null) chargingPoint.Name = updateRequest.Name;
        if (updateRequest.Note != null) chargingPoint.Note = updateRequest.Note;
        if (updateRequest.CountryName != null) chargingPoint.CountryName = updateRequest.CountryName;
        if (updateRequest.CityName != null) chargingPoint.CityName = updateRequest.CityName;
        if (updateRequest.Phone != null) chargingPoint.Phone = updateRequest.Phone;
        if (updateRequest.MethodPayment != null) chargingPoint.MethodPayment = updateRequest.MethodPayment;
        if (updateRequest.Price.HasValue) chargingPoint.Price = updateRequest.Price;
        if (updateRequest.FromTime != null) chargingPoint.FromTime = updateRequest.FromTime;
        if (updateRequest.ToTime != null) chargingPoint.ToTime = updateRequest.ToTime;
        if (updateRequest.ChargerSpeed.HasValue) chargingPoint.ChargerSpeed = updateRequest.ChargerSpeed;
        if (updateRequest.ChargersCount.HasValue) chargingPoint.ChargersCount = updateRequest.ChargersCount;
        if (updateRequest.Latitude.HasValue) chargingPoint.Latitude = updateRequest.Latitude.Value;
        if (updateRequest.Longitude.HasValue) chargingPoint.Longitude = updateRequest.Longitude.Value;
        if (updateRequest.ChargerPointTypeId.HasValue) chargingPoint.ChargerPointTypeId = updateRequest.ChargerPointTypeId.Value;
        if (updateRequest.StationTypeId.HasValue) chargingPoint.StationTypeId = updateRequest.StationTypeId.Value;
        if (updateRequest.OwnerPhone != null) chargingPoint.OwnerPhone = updateRequest.OwnerPhone;
        if (updateRequest.HasOffer.HasValue) chargingPoint.HasOffer = updateRequest.HasOffer.Value;
        if (updateRequest.Service != null) chargingPoint.Service = updateRequest.Service;
        if (updateRequest.OfferDescription != null) chargingPoint.OfferDescription = updateRequest.OfferDescription;
        if (updateRequest.Address != null) chargingPoint.Address = updateRequest.Address;
        if (updateRequest.ChargerBrand != null) chargingPoint.ChargerBrand = updateRequest.ChargerBrand;

        // 4. Apply icon change
        if (!string.IsNullOrEmpty(updateRequest.NewIcon))
        {
            // Delete old icon if exists
            if (!string.IsNullOrEmpty(chargingPoint.Icon))
            {
                uploadFileService.DeleteFiles(UploadFileFolders.CableChargingPoint, [chargingPoint.Icon], cancellationToken);
            }
            chargingPoint.Icon = updateRequest.NewIcon;
        }

        // 5. Apply plug type changes
        if (!string.IsNullOrEmpty(updateRequest.NewPlugTypeIds))
        {
            var newPlugTypeIds = JsonSerializer.Deserialize<List<int>>(updateRequest.NewPlugTypeIds);
            if (newPlugTypeIds != null && newPlugTypeIds.Any())
            {
                // Remove all existing plugs
                var existingPlugs = chargingPoint.ChargingPlugs.ToList();
                foreach (var plug in existingPlugs)
                {
                    context.ChargingPlugs.Remove(plug);
                }

                // Add new plugs
                foreach (var plugTypeId in newPlugTypeIds)
                {
                    context.ChargingPlugs.Add(new ChargingPlug
                    {
                        ChargingPointId = chargingPoint.Id,
                        PlugTypeId = plugTypeId
                    });
                }
            }
        }

        // 6. Apply attachment changes
        foreach (var attachmentChange in updateRequest.AttachmentChanges)
        {
            if (attachmentChange.AttachmentAction == AttachmentAction.Add)
            {
                // Add new attachment to charging point
                context.ChargingPointAttachments.Add(new ChargingPointAttachment
                {
                    ChargingPointId = chargingPoint.Id,
                    FileName = attachmentChange.FileName!,
                    FileExtension = attachmentChange.FileExtension,
                    FileSize = attachmentChange.FileSize,
                    ContentType = attachmentChange.ContentType
                });
            }
            else if (attachmentChange.AttachmentAction == AttachmentAction.Delete && attachmentChange.ExistingAttachmentId.HasValue)
            {
                // Soft delete the existing attachment
                var existingAttachment = await context.ChargingPointAttachments
                    .FirstOrDefaultAsync(x => x.Id == attachmentChange.ExistingAttachmentId.Value, cancellationToken);

                if (existingAttachment != null)
                {
                    existingAttachment.IsDeleted = true;
                    // Delete the physical file
                    uploadFileService.DeleteFiles(UploadFileFolders.CableAttachments, [existingAttachment.FileName], cancellationToken);
                }
            }
        }

        // 7. Update request status
        updateRequest.RequestStatus = RequestStatus.Approved;
        updateRequest.ReviewedByUserId = currentUserService.UserId;
        updateRequest.ReviewedAt = DateTime.UtcNow;

        await context.SaveChanges(cancellationToken);
    }
}
