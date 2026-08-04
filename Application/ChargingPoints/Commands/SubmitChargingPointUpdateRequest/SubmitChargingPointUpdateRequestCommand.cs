using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Domain.Enitites;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Application.Common.Extensions;
using Cable.Core;

namespace Application.ChargingPoints.Commands.SubmitChargingPointUpdateRequest;

// Owner-editable whitelist ONLY (operational data). Identity/trust fields —
// owner, isVerified, stationType, chargingPointType, hasOffer, premium — are
// deliberately NOT accepted here; those are admin-only via other endpoints.
public record SubmitChargingPointUpdateRequestCommand(
    int ChargingPointId,
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
    int? StatusId,
    string? OwnerPhone,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<int>? PlugTypeIds,
    List<int>? AttachmentsToDelete
) : IRequest<int>;

public class SubmitChargingPointUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService,
    INotificationService notificationService,
    Microsoft.Extensions.Logging.ILogger<SubmitChargingPointUpdateRequestCommandHandler> logger)
    : IRequestHandler<SubmitChargingPointUpdateRequestCommand, int>
{
    public async Task<int> Handle(SubmitChargingPointUpdateRequestCommand request, CancellationToken ct)
    {
        
        var chargingPoint = await context.ChargingPoints
            .Include(x => x.ChargingPlugs)
            .FirstOrDefaultAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, ct)
            ?? throw new NotFoundException($"Charging point not found: {request.ChargingPointId}");

        if (chargingPoint.OwnerId != currentUserService.UserId)
            throw new ForbiddenAccessException("You are not the owner of this charging point");

       
        var existingPendingRequest = await context.ChargingPointUpdateRequests
            .AnyAsync(x => x.ChargingPointId == request.ChargingPointId &&
                          x.RequestStatus == RequestStatus.Pending &&
                          !x.IsDeleted, ct);

        if (existingPendingRequest)
            throw new DataValidationException("ChargingPointId",
                "There is already a pending update request for this charging point");

        // 3. Create update request entity
        var normalizedPhone = request.Phone != null
            ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone
            : null;
        var normalizedOwnerPhone = request.OwnerPhone != null
            ? PhoneNumberUtility.NormalizePhoneNumber(request.OwnerPhone) ?? request.OwnerPhone
            : null;

        var updateRequest = new ChargingPointUpdateRequest
        {
            ChargingPointId = request.ChargingPointId,
            RequestedByUserId = currentUserService.UserId!.Value,
            RequestStatus = RequestStatus.Pending,

            // Store only changed fields
            Name = request.Name != chargingPoint.Name ? request.Name : null,
            Note = request.Note != chargingPoint.Note ? request.Note : null,
            CountryName = request.CountryName != chargingPoint.CountryName ? request.CountryName : null,
            CityName = request.CityName != chargingPoint.CityName ? request.CityName : null,
            Phone = normalizedPhone != null && normalizedPhone != chargingPoint.Phone ? normalizedPhone : null,
            MethodPayment = request.MethodPayment != chargingPoint.MethodPayment ? request.MethodPayment : null,
            Price = request.Price != chargingPoint.Price ? request.Price : null,
            FromTime = request.FromTime != chargingPoint.FromTime ? request.FromTime : null,
            ToTime = request.ToTime != chargingPoint.ToTime ? request.ToTime : null,
            ChargerSpeed = request.ChargerSpeed != chargingPoint.ChargerSpeed ? request.ChargerSpeed : null,
            ChargersCount = request.ChargersCount != chargingPoint.ChargersCount ? request.ChargersCount : null,
            Latitude = request.Latitude != chargingPoint.Latitude ? request.Latitude : null,
            Longitude = request.Longitude != chargingPoint.Longitude ? request.Longitude : null,
            StatusId = request.StatusId != chargingPoint.StatusId ? request.StatusId : null,
            OwnerPhone = normalizedOwnerPhone != null && normalizedOwnerPhone != chargingPoint.OwnerPhone ? normalizedOwnerPhone : null,
            Service = request.Service != chargingPoint.Service ? request.Service : null,
            OfferDescription = request.OfferDescription != chargingPoint.OfferDescription ? request.OfferDescription : null,
            Address = request.Address != chargingPoint.Address ? request.Address : null,
        };

        // Snapshot the station's current values for the changed fields — the
        // diff shown at review time uses this baseline, not the live station.
        updateRequest.OldValuesJson = UpdateRequestDiffHelper.BuildOldValuesSnapshot(updateRequest, chargingPoint);

        // 4. Handle plug type changes
        if (request.PlugTypeIds != null)
        {
            var currentPlugTypeIds = chargingPoint.ChargingPlugs
                .Where(x => !x.IsDeleted)
                .Select(x => x.PlugTypeId)
                .OrderBy(x => x)
                .ToList();

            var newPlugTypeIds = request.PlugTypeIds.OrderBy(x => x).ToList();

            if (!currentPlugTypeIds.SequenceEqual(newPlugTypeIds))
            {
                updateRequest.OldPlugTypeIds = JsonSerializer.Serialize(currentPlugTypeIds);
                updateRequest.NewPlugTypeIds = JsonSerializer.Serialize(newPlugTypeIds);
            }
        }

        // 5. Save update request
        context.ChargingPointUpdateRequests.Add(updateRequest);
        await context.SaveChanges(ct);

        // 6. Handle attachment deletions (if any specified)
        if (request.AttachmentsToDelete?.Any() == true)
        {
            foreach (var attachmentId in request.AttachmentsToDelete)
            {
                context.ChargingPointUpdateRequestAttachments.Add(new ChargingPointUpdateRequestAttachment
                {
                    UpdateRequestId = updateRequest.Id,
                    AttachmentAction = AttachmentAction.Delete,
                    ExistingAttachmentId = attachmentId
                });
            }
            await context.SaveChanges(ct);
        }

        // Best-effort: tell the admins there is a new request to review.
        await UpdateRequestNotifier.NotifyAdminsNewRequestAsync(
            context, notificationService, logger,
            updateRequest.Id, updateRequest.ChargingPointId, updateRequest.RequestedByUserId, ct);

        return updateRequest.Id;
    }
}
