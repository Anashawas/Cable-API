using Application.Common.Extensions;
using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.AddUpdateRequestAttachments;

public record AddUpdateRequestAttachmentsCommand(int UpdateRequestId, IFormFileCollection Files) : IRequest<int[]>;

public class AddUpdateRequestAttachmentsCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService) : IRequestHandler<AddUpdateRequestAttachmentsCommand, int[]>
{
    public async Task<int[]> Handle(AddUpdateRequestAttachmentsCommand request, CancellationToken cancellationToken)
    {
        // 1. Get the update request
        var updateRequest = await applicationDbContext.ChargingPointUpdateRequests
            .FirstOrDefaultAsync(x => x.Id == request.UpdateRequestId && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException($"Update request not found with id: {request.UpdateRequestId}");

        // 2. Verify ownership
        if (updateRequest.RequestedByUserId != currentUserService.UserId)
            throw new ForbiddenAccessException("You can only add attachments to your own update requests");

        // 3. Verify status is pending
        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("UpdateRequestId", "Attachments can only be added to pending requests");

        // 4. Upload files and create attachment records
        List<ChargingPointUpdateRequestAttachment> attachments = [];
        foreach (var file in request.Files)
        {
            var fileName = await uploadFileService.SaveFileAsync(file, UploadFileFolders.CableAttachments, cancellationToken);

            var attachment = new ChargingPointUpdateRequestAttachment
            {
                UpdateRequestId = request.UpdateRequestId,
                AttachmentAction = AttachmentAction.Add,
                FileName = fileName,
                FileExtension = file.GetFileExtension(),
                FileSize = file.Length,
                ContentType = file.ContentType
            };
            attachments.Add(attachment);
        }

        applicationDbContext.ChargingPointUpdateRequestAttachments.AddRange(attachments);
        await applicationDbContext.SaveChanges(cancellationToken);

        return attachments.Select(x => x.Id).ToArray();
    }
}
