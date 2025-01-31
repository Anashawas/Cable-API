using System.Net.Mail;
using Application.Common.Enums;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Cable.Core;
using Cable.Core.Exceptions;
using Infrastructrue.Common.Localization;
using Infrastructrue.Options;
using Infrastructrue.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Infrastructrue.UploadFiles;

public class UploadFileService(ICurrentUserService currentUserService, IOptions<UploadFileOptions> uploadFileOption)
    : IUploadFileService
{
    private readonly UploadFileOptions _uploadFileOption = uploadFileOption.Value;


    private string CurrentUserId => currentUserService.UserId.ToString()
                                    ?? throw new NotFoundException(nameof(currentUserService.UserId),
                                        currentUserService?.UserId ?? 0);


    public async Task<byte[]> GetFileAsync(UploadFileFolders folder, string fileName,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_uploadFileOption.FileUploadPath,
            UploadFilePathHelper.SetupFolderPath(folder, CurrentUserId, fileName));
        UploadFilePathHelper.CheckFileIsExist(filePath);
        return await File.ReadAllBytesAsync(
            filePath,
            cancellationToken);
    }

    public async Task<string> SaveFileAsync(IFormFile file, UploadFileFolders folder,
        CancellationToken cancellationToken)
    {
        if (file is { Length: <= 0 })
            throw new DataValidationException(nameof(file), Resources.FileNullOrEmpty);
        var fileName = file.GetFileName();
        var filePath = Path.Combine(_uploadFileOption.FileUploadPath,
            UploadFilePathHelper.SetupFolderPath(folder, CurrentUserId, fileName));

        UploadFilePathHelper.CheckFileIsExist(filePath);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);
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
        return UploadFilePathHelper.GetFilePath(_uploadFileOption.ServerUrl, folder, CurrentUserId, fileName);
    }

    public void DeleteFiles(UploadFileFolders uploadFileFolders, string[] filesNames,
        CancellationToken cancellationToken)
    {
        foreach (var fileName in filesNames)
        {
            var filePath = Path.Combine(_uploadFileOption.FileUploadPath,
                UploadFilePathHelper.SetupFolderPath(uploadFileFolders, CurrentUserId, fileName));

            UploadFilePathHelper.CheckFileIsExist(filePath);
            File.Delete(filePath);
        }
    }
}