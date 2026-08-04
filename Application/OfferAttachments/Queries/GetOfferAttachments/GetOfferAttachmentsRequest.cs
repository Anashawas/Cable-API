
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.OfferAttachments.Queries.GetOfferAttachments;

public record GetOfferAttachmentsRequest(int Id) : IRequest<List<UploadFile>>;

public class GetOfferAttachmentsQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetOfferAttachmentsRequest, List<UploadFile>>
{
    public async Task<List<UploadFile>> Handle(GetOfferAttachmentsRequest request,
        CancellationToken cancellationToken)
    {
        var attachments = await applicationDbContext.OfferAttachments
            .Where(x => x.OfferId == request.Id && !x.IsDeleted)
            .Select(x => new { x.FileName, x.ContentType, x.FileExtension, x.FileSize })
            .ToListAsync(cancellationToken);

        return attachments.Select(item => new UploadFile(item.FileName, item.ContentType,
            uploadFileService.GetFilePath(UploadFileFolders.CableOfferAttachments, item.FileName),
            item.FileExtension, item.FileSize)).ToList();
    }
}
