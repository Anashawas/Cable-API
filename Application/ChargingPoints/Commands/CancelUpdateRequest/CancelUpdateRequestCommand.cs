using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.CancelUpdateRequest;

public record CancelUpdateRequestCommand(int UpdateRequestId) : IRequest;

public class CancelUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<CancelUpdateRequestCommand>
{
    public async Task Handle(CancelUpdateRequestCommand request, CancellationToken cancellationToken)
    {
        // 1. Get the update request
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.AttachmentChanges)
            .FirstOrDefaultAsync(x => x.Id == request.UpdateRequestId && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(ChargingPointUpdateRequest), request.UpdateRequestId);

        // 2. Verify ownership
        if (updateRequest.RequestedByUserId != currentUserService.UserId)
            throw new ForbiddenAccessException("You can only cancel your own update requests");

        // 3. Verify status is pending
        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("UpdateRequestId", "Only pending requests can be cancelled");

        // 4. Delete uploaded files
        // Delete new icon if uploaded
        if (!string.IsNullOrEmpty(updateRequest.NewIcon))
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableChargingPoint, [updateRequest.NewIcon], cancellationToken);
        }

        // Delete new attachments that were uploaded
        var newAttachments = updateRequest.AttachmentChanges
            .Where(x => x.AttachmentAction == AttachmentAction.Add && !string.IsNullOrEmpty(x.FileName))
            .Select(x => x.FileName!)
            .ToArray();

        if (newAttachments.Length > 0)
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableAttachments, newAttachments, cancellationToken);
        }

        // 5. Soft delete the request
        updateRequest.IsDeleted = true;

        await context.SaveChanges(cancellationToken);
    }
}
