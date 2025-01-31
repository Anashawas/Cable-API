using Application.Common.Enums;
using Application.Common.Extensions;
using Cable.Core.Exceptions;
using MediatR.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.BannersAttachments.Commands;

public record AddBannerAttachmentsCommand(int BannerId, IFormFileCollection Files) : IRequest<int[]>;

public class AddBannerAttachmentsCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<AddBannerAttachmentsCommand, int[]>
{
    public async Task<int[]> Handle(AddBannerAttachmentsCommand request, CancellationToken cancellationToken)
    {
        var banner =
            await applicationDbContext.Banners.FirstOrDefaultAsync(x => x.Id == request.BannerId && !x.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException($"Can't find banner with id {request.BannerId}");
        
        var uploadedFiles = (List<BannerAttachment>) [];
        foreach (var file in request.Files)
        {
            var attachment = new BannerAttachment
            {
                BannerId = banner.Id,
                FileName = await uploadFileService.SaveFileAsync(file, UploadFileFolders.CableBanners, cancellationToken),
                FileSize = file.Length,
                ContentType = file.ContentType,
                FileExtension = file.GetFileExtension()
            };
            uploadedFiles.Add(attachment);
        }

        applicationDbContext.BannerAttachments.AddRange(uploadedFiles);
        await applicationDbContext.SaveChanges(cancellationToken);
        return uploadedFiles.Select(x => x.Id).ToArray();
    }
}