using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.RejectUpdateRequest;

public record RejectUpdateRequestCommand(int UpdateRequestId, string RejectionReason) : IRequest;

public class RejectUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<RejectUpdateRequestCommand>
{
    public async Task Handle(RejectUpdateRequestCommand request, CancellationToken cancellationToken)
    {
        // 1. Get the update request
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.AttachmentChanges)
            .FirstOrDefaultAsync(x => x.Id == request.UpdateRequestId && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(ChargingPointUpdateRequest), request.UpdateRequestId);

        // 2. Verify status is pending
        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("UpdateRequestId", "Only pending requests can be rejected");

        // 3. Delete uploaded files
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

        // 4. Update request status
        updateRequest.RequestStatus = RequestStatus.Rejected;
        updateRequest.ReviewedByUserId = currentUserService.UserId;
        updateRequest.ReviewedAt = DateTime.UtcNow;
        updateRequest.RejectionReason = request.RejectionReason;

        await context.SaveChanges(cancellationToken);
    }
}
