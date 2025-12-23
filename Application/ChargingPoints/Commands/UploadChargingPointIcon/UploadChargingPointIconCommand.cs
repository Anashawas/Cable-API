using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UploadChargingPointIcon;

public record UploadChargingPointIconCommand(IFormFile File,int Id):IRequest;

public class UploadChargingPointIconCommandHandler(IApplicationDbContext applicationDbContext,IUploadFileService uploadFileService)
    : IRequestHandler<UploadChargingPointIconCommand>
{
    public async Task Handle(UploadChargingPointIconCommand request, CancellationToken cancellationToken)
    {
       var chargingPoint = await applicationDbContext.ChargingPoints.FirstOrDefaultAsync(x=>x.Id == request.Id && !x.IsDeleted, cancellationToken);

       if (chargingPoint == null)
       {
           throw new NotFoundException($"Charging point not found with id : {request.Id}");
       }

       if (!string.IsNullOrEmpty(chargingPoint.Icon))
       {
            uploadFileService.DeleteFiles(UploadFileFolders.CableChargingPoint, [chargingPoint.Icon], cancellationToken);
       }
       if (request.File.Length > 0)
       {
           var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableChargingPoint, cancellationToken);
           chargingPoint.Icon = fileName;
       }
       await applicationDbContext.SaveChanges(cancellationToken);
    }
}