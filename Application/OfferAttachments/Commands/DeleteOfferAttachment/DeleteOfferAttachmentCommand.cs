
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.OfferAttachments.Commands.DeleteOfferAttachment;

public record DeleteOfferAttachmentCommand(int Id) : IRequest;

public class DeleteOfferAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<DeleteOfferAttachmentCommand>
{
    public async Task Handle(DeleteOfferAttachmentCommand request, CancellationToken cancellationToken)
    {
        var offerAttachments = await applicationDbContext.OfferAttachments
                                    .Where(x => x.OfferId == request.Id && !x.IsDeleted)
                                    .ToListAsync(cancellationToken)
                                ?? throw new NotFoundException(
                                    $"Can not find offer attachments with offer id {request.Id}");

        uploadFileService.DeleteFiles(UploadFileFolders.CableOfferAttachments,
            offerAttachments.Select(x => x.FileName).ToArray(), cancellationToken);

        applicationDbContext.OfferAttachments.RemoveRange(offerAttachments);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
