using Application.Common.Enums;
using Microsoft.AspNetCore.Http;

namespace Application.Common.Interfaces;

public interface IUploadFileService
{
    Task<byte[]> GetFileAsync(UploadFileFolders folder , string fileName, CancellationToken cancellationToken = default);
    Task<string> SaveFileAsync(IFormFile file, UploadFileFolders folder, CancellationToken cancellationToken = default);
    bool IsValidExtension(IFormFileCollection files);
    bool IsValidSize(IFormFileCollection files);
    string GetFilePath(UploadFileFolders folder, string fileName);

    void DeleteFiles(UploadFileFolders uploadFileFolders, string[] filesNames,
        CancellationToken cancellationToken);
}