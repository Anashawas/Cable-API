using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.DeleteServiceProviderAttachment;

public record DeleteServiceProviderAttachmentCommand(int Id) : IRequest;

public class DeleteServiceProviderAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteServiceProviderAttachmentCommand>
{
    public async Task Handle(DeleteServiceProviderAttachmentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider with id {request.Id} not found");

        if (serviceProvider.OwnerId != userId)
            throw new ForbiddenAccessException("You are not the owner of this service provider");

        var attachments = await applicationDbContext.ServiceProviderAttachments
                              .Where(x => x.ServiceProviderId == request.Id && !x.IsDeleted)
                              .ToListAsync(cancellationToken)
                          ?? throw new NotFoundException($"No attachments found for service provider with id {request.Id}");

        if (attachments.Count == 0)
            throw new NotFoundException($"No attachments found for service provider with id {request.Id}");

        uploadFileService.DeleteFiles(UploadFileFolders.CableServiceProvider,
            attachments.Select(x => x.FileName).ToArray(), cancellationToken);

        applicationDbContext.ServiceProviderAttachments.RemoveRange(attachments);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
