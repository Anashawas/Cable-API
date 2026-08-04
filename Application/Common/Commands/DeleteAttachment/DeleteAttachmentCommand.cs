using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Commands.DeleteAttachment;

public record DeleteAttachmentCommand(
    UploadFileFolders Folder,
    int Id
) : IRequest;

public class DeleteAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteAttachmentCommand>
{
    public async Task Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        _ = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        string? fileName = null;

        switch (request.Folder)
        {
            case UploadFileFolders.CableAttachments:
            {
                var attachment = await applicationDbContext.ChargingPointAttachments
                    .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Charging point attachment with id {request.Id} not found");
                fileName = attachment.FileName;
                applicationDbContext.ChargingPointAttachments.Remove(attachment);
                break;
            }
            case UploadFileFolders.CableBanners:
            {
                var attachment = await applicationDbContext.BannerAttachments
                    .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Banner attachment with id {request.Id} not found");
                fileName = attachment.FileName;
                applicationDbContext.BannerAttachments.Remove(attachment);
                break;
            }
            case UploadFileFolders.CableEmergencyService:
            {
                var attachment = await applicationDbContext.EmergencyServiceAttachments
                    .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Emergency service attachment with id {request.Id} not found");
                fileName = attachment.FileName;
                applicationDbContext.EmergencyServiceAttachments.Remove(attachment);
                break;
            }
            case UploadFileFolders.CableServiceProvider:
            {
                var attachment = await applicationDbContext.ServiceProviderAttachments
                    .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Service provider attachment with id {request.Id} not found");
                fileName = attachment.FileName;
                applicationDbContext.ServiceProviderAttachments.Remove(attachment);
                break;
            }
            case UploadFileFolders.CableOfferAttachments:
            {
                var attachment = await applicationDbContext.OfferAttachments
                    .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Offer attachment with id {request.Id} not found");
                fileName = attachment.FileName;
                applicationDbContext.OfferAttachments.Remove(attachment);
                break;
            }
            case UploadFileFolders.CableChargingPoint:
                throw new DataValidationException("Folder",
                    "CableChargingPoint folder is for icons, not attachments. Use the upload icon endpoint instead.");
            default:
                throw new DataValidationException("Folder", $"Folder '{request.Folder}' is not supported for attachment deletion");
        }

        // Delete physical file from disk
        uploadFileService.DeleteFiles(request.Folder, [fileName], cancellationToken);

        // Delete record from database
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
