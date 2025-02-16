﻿using Application.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.ChargingPointAttachments.Queries.GetAllChargingPointAttachmentsById;

public record GetAllChargingPointAttachmentsByIdRequest(int Id) : IRequest<List<UploadFile>>;

public class GetAllChargingPointAttachmentsByIdQueryHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllChargingPointAttachmentsByIdRequest, List<UploadFile>>
{
    public async Task<List<UploadFile>> Handle(GetAllChargingPointAttachmentsByIdRequest request,
        CancellationToken cancellationToken)
    {
        var files = await Task.WhenAll(
            applicationDbContext.ChargingPointAttachments
                .Where(x => x.ChargingPointId == request.Id).AsEnumerable()
                .Select(async item =>
                {
                    var fileContent = await uploadFileService.GetFileAsync(UploadFileFolders.CableAttachments ,item.FileName, cancellationToken);
                    return new UploadFile(item.FileName, item.ContentType, fileContent,
                        uploadFileService.GetFilePath(UploadFileFolders.CableAttachments,item.FileName),
                        item.FileExtension, item.FileSize);
                })
        );

        return files.ToList();
    }
}