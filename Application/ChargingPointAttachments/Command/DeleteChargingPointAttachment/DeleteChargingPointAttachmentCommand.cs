using Application.Common.Enums;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPointAttachments.Command.DeleteChargingPointAttachment;

public record DeleteChargingPointAttachmentCommand(int Id) : IRequest;

public class DeleteChargingPointAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<DeleteChargingPointAttachmentCommand>
{
    public async Task Handle(DeleteChargingPointAttachmentCommand request, CancellationToken cancellationToken)
    {
        var chargingPointAttachment = await applicationDbContext.ChargingPointAttachments
                                          .Where(x => x.ChargingPointId == request.Id && !x.IsDeleted)
                                          .ToListAsync(cancellationToken)
                                      ?? throw new NotFoundException(
                                          $"Can not find charging point attachment with id {request.Id}");
        
        uploadFileService.DeleteFiles(UploadFileFolders.CableBanners,
            chargingPointAttachment.Select(x => x.FileName).ToArray(), cancellationToken);

        applicationDbContext.ChargingPointAttachments.RemoveRange(chargingPointAttachment);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}