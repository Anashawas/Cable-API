using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServiceAttachments.Commands.DeleteEmergencyServiceAttachment;

public record DeleteEmergencyServiceAttachmentCommand(int Id) : IRequest;

public class DeleteEmergencyServiceAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<DeleteEmergencyServiceAttachmentCommand>
{
    public async Task Handle(DeleteEmergencyServiceAttachmentCommand request, CancellationToken cancellationToken)
    {
        var emergencyServiceAttachments = await applicationDbContext.EmergencyServiceAttachments
            .Where(x => x.EmergencyServiceId == request.Id && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        if (emergencyServiceAttachments.Count == 0)
            throw new NotFoundException($"Cannot find emergency service attachments with id {request.Id}");

        uploadFileService.DeleteFiles(
            UploadFileFolders.CableEmergencyService,
            emergencyServiceAttachments.Select(x => x.FileName).ToArray(),
            cancellationToken);

        applicationDbContext.EmergencyServiceAttachments.RemoveRange(emergencyServiceAttachments);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
