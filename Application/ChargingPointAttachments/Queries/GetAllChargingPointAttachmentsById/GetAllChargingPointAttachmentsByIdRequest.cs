
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.ChargingPointAttachments.Queries.GetAllChargingPointAttachmentsById;

public record GetAllChargingPointAttachmentsByIdRequest(int Id) : IRequest<List<UploadFile>>;

public class GetAllChargingPointAttachmentsByIdQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllChargingPointAttachmentsByIdRequest, List<UploadFile>>
{
    public async Task<List<UploadFile>> Handle(GetAllChargingPointAttachmentsByIdRequest request,
        CancellationToken cancellationToken)
    {
        var attachments = await applicationDbContext.ChargingPointAttachments
            .Where(x => x.ChargingPointId == request.Id && !x.IsDeleted)
            .Select(x => new { x.FileName, x.ContentType, x.FileExtension, x.FileSize })
            .ToListAsync(cancellationToken);

        return attachments.Select(item => new UploadFile(item.FileName, item.ContentType,
            uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, item.FileName),
            item.FileExtension, item.FileSize)).ToList();
    }
}