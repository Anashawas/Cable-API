using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsTypes.Commands.UploadCarTypeIcon;

public record UploadCarTypeIconCommand(IFormFile File, int Id) : IRequest;

public class UploadCarTypeIconCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<UploadCarTypeIconCommand>
{
    public async Task Handle(UploadCarTypeIconCommand request, CancellationToken cancellationToken)
    {
        var carType = await applicationDbContext.CarTypes
                          .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                      ?? throw new NotFoundException($"can not find car type with id {request.Id}");

        if (!string.IsNullOrEmpty(carType.Icon))
        {
            uploadFileService.DeleteFiles(UploadFileFolders.CableCarTypes, [carType.Icon], cancellationToken);
        }

        if (request.File.Length > 0)
        {
            var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableCarTypes, cancellationToken);
            carType.Icon = fileName;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
