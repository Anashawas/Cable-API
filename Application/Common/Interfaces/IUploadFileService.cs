using Microsoft.AspNetCore.Http;

namespace Application.Common.Interfaces;

public interface IUploadFileService
{
    Task<byte[]> GetFileAsync(string path, CancellationToken cancellationToken = default);
    Task<string> SaveFileAsync(IFormFile file, CancellationToken cancellationToken = default);
    bool IsValidExtension (IFormFileCollection files);
    bool IsValidSize (IFormFileCollection files);

}