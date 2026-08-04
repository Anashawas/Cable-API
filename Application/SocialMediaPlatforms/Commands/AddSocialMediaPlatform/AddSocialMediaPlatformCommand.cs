using Application.Common.Extensions;
using Cable.Core.Emuns;
using Microsoft.AspNetCore.Http;

namespace Application.SocialMediaPlatforms.Commands.AddSocialMediaPlatform;

public record AddSocialMediaPlatformCommand(
    string     Name,
    string?    NameAr,
    int        DisplayOrder,
    IFormFile? Icon
) : IRequest<int>;

public class AddSocialMediaPlatformCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<AddSocialMediaPlatformCommand, int>
{
    public async Task<int> Handle(AddSocialMediaPlatformCommand request, CancellationToken cancellationToken)
    {
        var platform = new SocialMediaPlatform
        {
            Name         = request.Name,
            NameAr       = request.NameAr,
            DisplayOrder = request.DisplayOrder,
            IsActive     = true
        };

        if (request.Icon is { Length: > 0 })
        {
            platform.IconFileName    = await uploadFileService.SaveFileAsync(
                                            request.Icon,
                                            UploadFileFolders.CableSocialMediaIcons,
                                            cancellationToken);
            platform.IconExtension   = request.Icon.GetFileExtension();
            platform.IconContentType = request.Icon.ContentType;
            platform.IconFileSize    = request.Icon.Length;
        }

        applicationDbContext.SocialMediaPlatforms.Add(platform);
        await applicationDbContext.SaveChanges(cancellationToken);

        return platform.Id;
    }
}
