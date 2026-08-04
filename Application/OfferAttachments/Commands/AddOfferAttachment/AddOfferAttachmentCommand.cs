
using Application.Common.Extensions;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.OfferAttachments.Commands.AddOfferAttachment;

public record AddOfferAttachmentCommand(int Id, IFormFileCollection Files) : IRequest<int[]>;

public class AddOfferAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService) : IRequestHandler<AddOfferAttachmentCommand, int[]>
{
    public async Task<int[]> Handle(AddOfferAttachmentCommand request, CancellationToken cancellationToken)
    {
        var offer =
            await applicationDbContext.ProviderOffers.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken) ??
            throw new NotFoundException(nameof(ProviderOffer), request.Id);

        List<OfferAttachment> offerAttachments = [];
        foreach (var file in request.Files)
        {
            var offerAttachment = new OfferAttachment
            {
                FileName = await uploadFileService.SaveFileAsync(file, UploadFileFolders.CableOfferAttachments,
                    cancellationToken),
                FileExtension = file.GetFileExtension(),
                FileSize = file.Length,
                OfferId = request.Id,
                ContentType = file.ContentType,
            };
            offerAttachments.Add(offerAttachment);
        }

        applicationDbContext.OfferAttachments.AddRange(offerAttachments);
        await applicationDbContext.SaveChanges(cancellationToken);
        return offerAttachments.Select(x => x.Id).ToArray();
    }
}
