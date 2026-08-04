using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.SocialMediaPlatforms.Queries.GetAllSocialMediaPlatforms;

public record GetAllSocialMediaPlatformsRequest(bool ActiveOnly = true)
    : IRequest<List<SocialMediaPlatformDto>>;

public class GetAllSocialMediaPlatformsQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllSocialMediaPlatformsRequest, List<SocialMediaPlatformDto>>
{
    public async Task<List<SocialMediaPlatformDto>> Handle(
        GetAllSocialMediaPlatformsRequest request,
        CancellationToken cancellationToken)
    {
        var query = applicationDbContext.SocialMediaPlatforms
            .AsNoTracking()
            .Where(p => !p.IsDeleted);

        if (request.ActiveOnly)
            query = query.Where(p => p.IsActive);

        var rows = await query
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.NameAr,
                p.IconFileName,
                p.DisplayOrder,
                p.IsActive
            })
            .ToListAsync(cancellationToken);

        return rows.Select(p => new SocialMediaPlatformDto(
            p.Id,
            p.Name,
            p.NameAr,
            string.IsNullOrEmpty(p.IconFileName)
                ? null
                : uploadFileService.GetFilePath(UploadFileFolders.CableSocialMediaIcons, p.IconFileName),
            p.DisplayOrder,
            p.IsActive
        )).ToList();
    }
}
