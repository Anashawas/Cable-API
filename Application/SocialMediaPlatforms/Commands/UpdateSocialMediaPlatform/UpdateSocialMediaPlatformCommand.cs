using Application.Common.Extensions;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.SocialMediaPlatforms.Commands.UpdateSocialMediaPlatform;

public record UpdateSocialMediaPlatformCommand(
    int        Id,
    string     Name,
    string?    NameAr,
    int        DisplayOrder,
    bool       IsActive,
    IFormFile? Icon
) : IRequest;

public class UpdateSocialMediaPlatformCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<UpdateSocialMediaPlatformCommand>
{
    public async Task Handle(UpdateSocialMediaPlatformCommand request, CancellationToken cancellationToken)
    {
        var platform = await applicationDbContext.SocialMediaPlatforms
                           .FirstOrDefaultAsync(p => p.Id == request.Id && !p.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException(nameof(SocialMediaPlatform), request.Id);

        platform.Name         = request.Name;
        platform.NameAr       = request.NameAr;
        platform.DisplayOrder = request.DisplayOrder;
        platform.IsActive     = request.IsActive;

        // Replace icon if a new one was supplied.
        if (request.Icon is { Length: > 0 })
        {
            // Best-effort delete of the old icon file (if any).
            if (!string.IsNullOrEmpty(platform.IconFileName))
            {
                try
                {
                    uploadFileService.DeleteFiles(
                        UploadFileFolders.CableSocialMediaIcons,
                        new[] { platform.IconFileName },
                        cancellationToken);
                }
                catch
                {
                    // Ignore if the previous file is missing on disk — we still want the update to succeed.
                }
            }

            platform.IconFileName    = await uploadFileService.SaveFileAsync(
                                            request.Icon,
                                            UploadFileFolders.CableSocialMediaIcons,
                                            cancellationToken);
            platform.IconExtension   = request.Icon.GetFileExtension();
            platform.IconContentType = request.Icon.ContentType;
            platform.IconFileSize    = request.Icon.Length;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
