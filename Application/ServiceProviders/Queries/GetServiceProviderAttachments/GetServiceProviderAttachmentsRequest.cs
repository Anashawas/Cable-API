using Cable.Core.Emuns;

namespace Application.ServiceProviders.Queries.GetServiceProviderAttachments;

public record GetServiceProviderAttachmentsRequest(int Id) : IRequest<List<UploadFile>>;

public class GetServiceProviderAttachmentsRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetServiceProviderAttachmentsRequest, List<UploadFile>>
{
    public async Task<List<UploadFile>> Handle(GetServiceProviderAttachmentsRequest request,
        CancellationToken cancellationToken)
    {
        var files = await Task.WhenAll(
            applicationDbContext.ServiceProviderAttachments
                .Where(x => x.ServiceProviderId == request.Id && !x.IsDeleted).AsEnumerable()
                .Select(async item => new UploadFile(item.FileName, item.ContentType,
                    uploadFileService.GetFilePath(UploadFileFolders.CableServiceProvider, item.FileName),
                    item.FileExtension, item.FileSize))
        );

        return files.ToList();
    }
}
