using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Enums;
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
            .AsNoTracking()
            .Include(x => x.ChargingPoint)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.AttachmentChanges)
                .ThenInclude(ac => ac.ExistingAttachment)
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(ChargingPointUpdateRequest), request.Id);

        var names = await UpdateRequestDiffHelper.LoadNamesAsync(context, [updateRequest], cancellationToken);
        var diff = UpdateRequestDiffHelper.Build(updateRequest, names, uploadFileService);

        var attachmentChanges = updateRequest.AttachmentChanges.Select(ac => new AttachmentChangeDto(
            ac.Id,
            ac.AttachmentAction,
            ac.FileName,
            ac.AttachmentAction == AttachmentAction.Add && !string.IsNullOrEmpty(ac.FileName)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, ac.FileName)
                : ac.ExistingAttachment != null
                    ? uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, ac.ExistingAttachment.FileName)
                    : null,
            ac.ExistingAttachmentId
        )).ToList();

        return new GetUpdateRequestByIdDto(
            updateRequest.Id,
            updateRequest.ChargingPointId,
            updateRequest.ChargingPoint.Name,
            updateRequest.RequestedByUserId,
            updateRequest.RequestedBy?.Name,
            updateRequest.RequestedBy?.Phone,
            updateRequest.RequestStatus,
            updateRequest.CreatedAt,
            updateRequest.ReviewedAt,
            updateRequest.ReviewedByUserId,
            updateRequest.ReviewedBy?.Name,
            updateRequest.RejectionReason,
            diff.Changes,
            diff.Attachments,
            diff.RiskFlags,
            attachmentChanges
        );
    }
}
