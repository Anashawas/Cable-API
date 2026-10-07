using System.Net.Mail;

using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Infrastructrue.Common.Localization;
using Infrastructrue.Options;
using Infrastructrue.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Infrastructrue.UploadFiles;

public class UploadFileService(ICurrentUserService currentUserService, IHttpContextAccessor httpContextAccessor, IOptions<UploadFileOptions> uploadFileOption)
    : IUploadFileService
{
    private readonly UploadFileOptions _uploadFileOption = uploadFileOption.Value;


    private string CurrentUserId => currentUserService.UserId.ToString()
                                    ?? throw new NotFoundException(nameof(currentUserService.UserId),
                                        currentUserService?.UserId ?? 0);


    public async Task<byte[]> GetFileAsync(UploadFileFolders folder, string fileName,
        CancellationToken cancellationToken)
    {
        // var isPublicFolder = UploadFilePathHelper.IsPublicFolder(folder);
        var folderName = folder.ToString();
        
        var filePath = 
             Path.Combine(_uploadFileOption.FileUploadPath,  folderName,  fileName);

        UploadFilePathHelper.CheckFileIsExist(filePath);

        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }
    public async Task<string> SaveFileAsync(IFormFile file, UploadFileFolders folder,
        CancellationToken cancellationToken)
    {
        if (file is { Length: <= 0 })
            throw new DataValidationException(nameof(file), Resources.FileNullOrEmpty);
        var fileName =file.GetFileName();

        var filePath =
            UploadFilePathHelper.SetupFolderPath(_uploadFileOption.FileUploadPath, folder, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);
        return fileName;
    }

    public async Task<string> SaveBytesAsync(byte[] content, string extension, UploadFileFolders folder,
        CancellationToken cancellationToken = default)
    {
        if (content is not { Length: > 0 })
            throw new DataValidationException(nameof(content), Resources.FileNullOrEmpty);
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var fileName = $"{Guid.CreateVersion7()}{ext}";
        var filePath = UploadFilePathHelper.SetupFolderPath(_uploadFileOption.FileUploadPath, folder, fileName);
        await File.WriteAllBytesAsync(filePath, content, cancellationToken);
        return fileName;
    }

    public bool IsValidExtension(IFormFileCollection files)
    {
        if (files.Any())
            return !(from file in files
                let fileExtension = file.GetFileExtension()
                where !_uploadFileOption.AllowedExtensions.Contains(fileExtension)
                select file).Any();

        return false;
    }

    public bool IsValidSize(IFormFileCollection files) =>
        !(files.Where(file => file.Length > _uploadFileOption.LimitSize)).Any();

    public string GetFilePath(UploadFileFolders folder, string fileName)
    {
        // Config first, request host only as fallback: URLs built (and sometimes
        // STORED, e.g. announcement imageUrl) must never capture whatever host the
        // API happened to be reached on — a local upload once persisted a
        // "localhost:5202" URL that phones couldn't load.
        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = !string.IsNullOrWhiteSpace(_uploadFileOption.ServerUrl)
            ? _uploadFileOption.ServerUrl.TrimEnd('/')
            : request != null
                ? $"{request.Scheme}://{request.Host}"
                : string.Empty;
        return UploadFilePathHelper.GetFilePath(baseUrl, folder, fileName);
    }

    public void DeleteFiles(UploadFileFolders uploadFileFolders, string[] filesNames,
        CancellationToken cancellationToken)
    {
        // var isPublicFolder = UploadFilePathHelper.IsPublicFolder(uploadFileFolders);
        // var basePath = isPublicFolder ? nameof(FolderType.Public) : nameof(FolderType.Private);
        var folderName = uploadFileFolders.ToString();
        
        foreach (var fileName in filesNames)
        {
            var filePath = 
                Path.Combine(_uploadFileOption.FileUploadPath,  folderName, fileName.ToString());

            UploadFilePathHelper.CheckFileIsExist(filePath);
            File.Delete(filePath);
        }
    }



}