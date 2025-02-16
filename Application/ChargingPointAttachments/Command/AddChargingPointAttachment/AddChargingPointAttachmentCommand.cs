﻿using Application.Common.Enums;
using Application.Common.Extensions;
using Microsoft.AspNetCore.Http;

namespace Application.ChargingPointAttachments.Command;

public record AddChargingPointAttachmentCommand(int Id, IFormFileCollection Files) : IRequest<int[]>;

public class AddChargingPointAttachmentCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService) : IRequestHandler<AddChargingPointAttachmentCommand, int[]>
{
    public async Task<int[]> Handle(AddChargingPointAttachmentCommand request, CancellationToken cancellationToken)
    {
        List<ChargingPointAttachment> chargingPointAttachments = [];
        foreach (var file in request.Files)
        {
            var chargingPointAttachment = new ChargingPointAttachment
            {
                FileName = await uploadFileService.SaveFileAsync(file, UploadFileFolders.CableAttachments, cancellationToken),
                FileExtension = file.GetFileExtension(),
                FileSize = file.Length,
                ChargingPointId = request.Id,
                ContentType = file.ContentType,
            };
            chargingPointAttachments.Add(chargingPointAttachment);
        }

        applicationDbContext.ChargingPointAttachments.AddRange(chargingPointAttachments);
        await applicationDbContext.SaveChanges(cancellationToken);
        return chargingPointAttachments.Select(x => x.Id).ToArray();
    }
}