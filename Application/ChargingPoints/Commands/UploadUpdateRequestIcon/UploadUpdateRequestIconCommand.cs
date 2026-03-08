using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UploadUpdateRequestIcon;

public record UploadUpdateRequestIconCommand(IFormFile File, int UpdateRequestId) : IRequest;

public class UploadUpdateRequestIconCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService)
    : IRequestHandler<UploadUpdateRequestIconCommand>
{
    public async Task Handle(UploadUpdateRequestIconCommand request, CancellationToken cancellationToken)
    {
        // 1. Get the update request
        var updateRequest = await applicationDbContext.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
            .FirstOrDefaultAsync(x => x.Id == request.UpdateRequestId && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException($"Update request not found with id: {request.UpdateRequestId}");

        // 2. Verify ownership
        if (updateRequest.RequestedByUserId != currentUserService.UserId)
            throw new ForbiddenAccessException("You can only upload icon for your own update requests");

        // 3. Verify status is pending
        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("UpdateRequestId", "Icon can only be uploaded for pending requests");

        // 4. Delete old uploaded icon if exists (not the original charging point icon)
        if (!string.IsNullOrEmpty(updateRequest.NewIcon))
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableChargingPoint, [updateRequest.NewIcon], cancellationToken);
        }

        // 5. Upload new icon
        if (request.File.Length > 0)
        {
            var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableChargingPoint, cancellationToken);
            updateRequest.NewIcon = fileName;
            updateRequest.OldIcon = updateRequest.ChargingPoint.Icon; // Track what we're replacing
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
