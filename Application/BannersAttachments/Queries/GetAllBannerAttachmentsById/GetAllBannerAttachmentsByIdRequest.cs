
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.BannersAttachments.Queries.GetAllBannerAttachmentsById;

public record GetAllBannerAttachmentsByIdRequest(int Id) : IRequest<List<string>>;

public class GetAllBannerAttachmentsByIdQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllBannerAttachmentsByIdRequest, List<string>>
{
    public async Task<List<string>> Handle(GetAllBannerAttachmentsByIdRequest request,
        CancellationToken cancellationToken)
        => await applicationDbContext.BannerAttachments
            .Where(x => x.BannerId == request.Id)
            .Select(x => uploadFileService.GetFilePath(UploadFileFolders.CableBanners, x.FileName))
            .ToListAsync(cancellationToken);
}