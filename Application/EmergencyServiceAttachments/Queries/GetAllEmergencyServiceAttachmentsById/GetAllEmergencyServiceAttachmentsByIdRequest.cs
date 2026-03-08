using Application.Common.Interfaces;
using Application.Common.Models;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServiceAttachments.Queries.GetAllEmergencyServiceAttachmentsById;

public record GetAllEmergencyServiceAttachmentsByIdRequest(int Id) : IRequest<List<UploadFile>>;

public class GetAllEmergencyServiceAttachmentsByIdQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllEmergencyServiceAttachmentsByIdRequest, List<UploadFile>>
{
    public async Task<List<UploadFile>> Handle(GetAllEmergencyServiceAttachmentsByIdRequest request,
        CancellationToken cancellationToken)
    {
        var files = await Task.WhenAll(
            applicationDbContext.EmergencyServiceAttachments
                .Where(x => x.EmergencyServiceId == request.Id && !x.IsDeleted)
                .AsEnumerable()
                .Select(async item => new UploadFile(
                    item.FileName,
                    item.ContentType,
                    uploadFileService.GetFilePath(UploadFileFolders.CableEmergencyService, item.FileName),
                    item.FileExtension,
                    item.FileSize))
        );

        return files.ToList();
    }
}
