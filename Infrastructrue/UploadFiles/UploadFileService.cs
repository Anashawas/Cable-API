using System.Net.Mail;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Cable.Core;
using Cable.Core.Exceptions;
using Infrastructrue.Common.Localization;
using Infrastructrue.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Infrastructrue.UploadFiles;

public class UploadFileService(
    ICurrentUserService currentUserService,
    IOptions<UploadFileOptions> uploadFileOption) : IUploadFileService
{
    private readonly UploadFileOptions _uploadFileOption = uploadFileOption.Value;

    private string CurrentUserId => currentUserService.UserId.ToString()
                                    ?? throw new NotFoundException(
                                        $"Can not find user with id {currentUserService.UserId}");

    private string UserUploadPath => Path.Combine(_uploadFileOption.Path, CurrentUserId);


    public async Task<byte[]> GetFileAsync(string fileName, CancellationToken cancellationToken )
    {
        var filePath = Path.Combine(UserUploadPath, fileName);
        if (!File.Exists(filePath))
            throw new NotFoundException($"File not found: {fileName}");
        
        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    public async Task<string> SaveFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is { Length: <= 0 })
            throw new DataValidationException(nameof(file), Resources.FileNullOrEmpty);


        if (!Directory.Exists(UserUploadPath))
            Directory.CreateDirectory(UserUploadPath);

        var filePath = Path.Combine(UserUploadPath, file.GetFileName());
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);

        return filePath;
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
}