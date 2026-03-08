using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceCategories.Commands.UploadServiceCategoryIcon;

public record UploadServiceCategoryIconCommand(IFormFile File, int Id) : IRequest;

public class UploadServiceCategoryIconCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<UploadServiceCategoryIconCommand>
{
    public async Task Handle(UploadServiceCategoryIconCommand request, CancellationToken cancellationToken)
    {
        var serviceCategory = await applicationDbContext.ServiceCategories
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);

        if (serviceCategory == null)
        {
            throw new NotFoundException($"Service category not found with id : {request.Id}");
        }

        if (!string.IsNullOrEmpty(serviceCategory.IconUrl))
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableServiceProvider, [serviceCategory.IconUrl], cancellationToken);
        }

        if (request.File.Length > 0)
        {
            var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableServiceProvider, cancellationToken);
            serviceCategory.IconUrl = fileName;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
