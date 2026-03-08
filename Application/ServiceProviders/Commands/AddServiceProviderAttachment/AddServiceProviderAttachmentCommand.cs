using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.AddServiceProviderAttachment;

public record AddServiceProviderAttachmentCommand(int Id, IFormFileCollection Files) : IRequest<int[]>;

public class AddServiceProviderAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService)
    : IRequestHandler<AddServiceProviderAttachmentCommand, int[]>
{
    public async Task<int[]> Handle(AddServiceProviderAttachmentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider not found with id {request.Id}");

        if (serviceProvider.OwnerId != userId)
            throw new ForbiddenAccessException("You are not the owner of this service provider");

        List<ServiceProviderAttachment> attachments = [];
        foreach (var file in request.Files)
        {
            var attachment = new ServiceProviderAttachment
            {
                FileName = await uploadFileService.SaveFileAsync(file, UploadFileFolders.CableServiceProvider, cancellationToken),
                FileExtension = file.GetFileExtension(),
                FileSize = file.Length,
                ServiceProviderId = request.Id,
                ContentType = file.ContentType,
            };
            attachments.Add(attachment);
        }

        applicationDbContext.ServiceProviderAttachments.AddRange(attachments);
        await applicationDbContext.SaveChanges(cancellationToken);
        return attachments.Select(x => x.Id).ToArray();
    }
}
