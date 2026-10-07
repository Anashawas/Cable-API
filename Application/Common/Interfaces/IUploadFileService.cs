
using Cable.Core.Emuns;
using Microsoft.AspNetCore.Http;

namespace Application.Common.Interfaces;

public interface IUploadFileService
{
    Task<byte[]> GetFileAsync(UploadFileFolders folder , string fileName, CancellationToken cancellationToken = default);
    Task<string> SaveFileAsync(IFormFile file, UploadFileFolders folder, CancellationToken cancellationToken = default);
    bool IsValidExtension(IFormFileCollection files);
    bool IsValidSize(IFormFileCollection files);
    string GetFilePath(UploadFileFolders folder, string fileName);

    /// <summary>Stores generated content (e.g. a receipt PDF) under a fresh unique name; returns that name.</summary>
    Task<string> SaveBytesAsync(byte[] content, string extension, UploadFileFolders folder, CancellationToken cancellationToken = default);

    void DeleteFiles(UploadFileFolders uploadFileFolders, string[] filesNames,
        CancellationToken cancellationToken);
}