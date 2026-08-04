using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.SocialLinks.Queries.GetSocialLinksByProvider;

public record GetSocialLinksByProviderRequest(string ProviderType, int ProviderId)
    : IRequest<List<SocialLinkDto>>;

public class GetSocialLinksByProviderQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetSocialLinksByProviderRequest, List<SocialLinkDto>>
{
    public async Task<List<SocialLinkDto>> Handle(
        GetSocialLinksByProviderRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await applicationDbContext.SocialLinks
            .AsNoTracking()
            .Where(l => l.ProviderType == request.ProviderType
                     && l.ProviderId   == request.ProviderId
                     && !l.IsDeleted)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.Id)
            .Select(l => new
            {
                l.Id,
                l.ProviderType,
                l.ProviderId,
                l.SocialMediaPlatformId,
                PlatformName     = l.SocialMediaPlatform.Name,
                PlatformNameAr   = l.SocialMediaPlatform.NameAr,
                PlatformIconFile = l.SocialMediaPlatform.IconFileName,
                l.Url,
                l.DisplayOrder
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new SocialLinkDto(
            r.Id,
            r.ProviderType,
            r.ProviderId,
            r.SocialMediaPlatformId,
            r.PlatformName,
            r.PlatformNameAr,
            string.IsNullOrEmpty(r.PlatformIconFile)
                ? null
                : uploadFileService.GetFilePath(UploadFileFolders.CableSocialMediaIcons, r.PlatformIconFile),
            r.Url,
            r.DisplayOrder
        )).ToList();
    }
}
