using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.UploadOfferImage;

public record UploadOfferImageCommand(IFormFile File, int Id) : IRequest;

public class UploadOfferImageCommandHandler(IApplicationDbContext applicationDbContext, IUploadFileService uploadFileService)
    : IRequestHandler<UploadOfferImageCommand>
{
    public async Task Handle(UploadOfferImageCommand request, CancellationToken cancellationToken)
    {
        var offer = await applicationDbContext.ProviderOffers.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);

        if (offer == null)
        {
            throw new NotFoundException($"Offer not found with id : {request.Id}");
        }

        if (!string.IsNullOrEmpty(offer.ImageUrl))
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableOfferAttachments, [offer.ImageUrl], cancellationToken);
        }
        if (request.File.Length > 0)
        {
            var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableOfferAttachments, cancellationToken);
            offer.ImageUrl = fileName;
        }
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
