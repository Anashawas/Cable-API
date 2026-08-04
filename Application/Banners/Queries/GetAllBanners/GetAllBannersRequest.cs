
using Application.Common.Models;
using Cable.Core.Emuns;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Queries.GetAllBanners;

public record GetAllBannersRequest(int? Page = null, int? PageSize = null) : IRequest<PagedResult<GetAllBannersDto>>;

public class GetAllBannersQueryHandler(IApplicationDbContext applicationDbContext, IUploadFileService uploadFileService)
    : IRequestHandler<GetAllBannersRequest, PagedResult<GetAllBannersDto>>
{
    public async Task<PagedResult<GetAllBannersDto>> Handle(GetAllBannersRequest request, CancellationToken cancellationToken)
    {
        var result = await applicationDbContext.Banners.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.BannerAttachments)
            .Include(x => x.BannerDurations).ToListAsync(cancellationToken);
        var banners = result.Select(x => new GetAllBannersDto(x.Id, x.Name, x.Phone,x.ActionType,x.ActionUrl
            , x.BannerDurations.Select(b => new BannerDurationSummery(b.Id, b.StartDate, b.EndDate)).ToList(),
            x.BannerAttachments.Select(a => new BannerAttachmentSummery(
                a.Id, a.ContentType, a.FileName, a.FileSize, a.FileExtension,
                uploadFileService.GetFilePath(folder: UploadFileFolders.CableBanners, fileName: a.FileName))).ToList()
        )).ToList();
        return banners.ToOptionallyPaginated(request.Page, request.PageSize);
    }
}