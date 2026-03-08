using Application.Common.Extensions;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServiceAttachments.Commands.AddEmergencyServiceAttachment;

public record AddEmergencyServiceAttachmentCommand(int Id, IFormFileCollection Files) : IRequest<int[]>;

public class AddEmergencyServiceAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<AddEmergencyServiceAttachmentCommand, int[]>
{
    public async Task<int[]> Handle(AddEmergencyServiceAttachmentCommand request, CancellationToken cancellationToken)
    {
        var emergencyService = await applicationDbContext.EmergencyServices
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(EmergencyService), request.Id);

        List<EmergencyServiceAttachment> emergencyServiceAttachments = [];

        foreach (var file in request.Files)
        {
            var attachment = new EmergencyServiceAttachment
            {
                FileName = await uploadFileService.SaveFileAsync(
                    file,
                    UploadFileFolders.CableEmergencyService,
                    cancellationToken),
                FileExtension = file.GetFileExtension(),
                FileSize = file.Length,
                EmergencyServiceId = request.Id,
                ContentType = file.ContentType,
            };
            emergencyServiceAttachments.Add(attachment);
        }

        applicationDbContext.EmergencyServiceAttachments.AddRange(emergencyServiceAttachments);
        await applicationDbContext.SaveChanges(cancellationToken);

        return emergencyServiceAttachments.Select(x => x.Id).ToArray();
    }
}
