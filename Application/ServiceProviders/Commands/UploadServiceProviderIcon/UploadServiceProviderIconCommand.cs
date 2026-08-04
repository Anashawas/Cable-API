using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.UploadServiceProviderIcon;

public record UploadServiceProviderIconCommand(IFormFile File, int Id) : IRequest;

public class UploadServiceProviderIconCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<UploadServiceProviderIconCommand>
{
    public async Task Handle(UploadServiceProviderIconCommand request, CancellationToken cancellationToken)
    {
        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider not found with id : {request.Id}");

        // Delete old icon if exists
        if (!string.IsNullOrEmpty(serviceProvider.Icon))
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableServiceProvider, [serviceProvider.Icon], cancellationToken);
        }

        // Upload new icon
        if (request.File.Length > 0)
        {
            var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableServiceProvider, cancellationToken);
            serviceProvider.Icon = fileName;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
