using Application.Common.Enums;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.BannersAttachments.Commands.DeleteBannerAttachment;

public record DeleteBannerAttachmentCommand(int Id) : IRequest;

public class DeleteBannerAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<DeleteBannerAttachmentCommand>
{
    public async Task Handle(DeleteBannerAttachmentCommand request, CancellationToken cancellationToken)
    {
        var bannerAttachments = await applicationDbContext.BannerAttachments.Where(x => x.BannerId == request.Id)
            .ToListAsync(cancellationToken);
        if (bannerAttachments == null)
        {
            throw new NotFoundException(nameof(BannerAttachment), request.Id);
        }

        uploadFileService.DeleteFiles(UploadFileFolders.CableBanners,
            bannerAttachments.Select(x => x.FileName).ToArray(), cancellationToken);
        
        applicationDbContext.BannerAttachments.RemoveRange(bannerAttachments);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}