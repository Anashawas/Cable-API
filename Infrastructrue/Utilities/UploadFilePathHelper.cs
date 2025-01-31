using Application.Common.Enums;
using Cable.Core.Exceptions;

namespace Infrastructrue.Utilities;

public static class UploadFilePathHelper
{
    public static string SetupFolderPath(UploadFileFolders folder, string userId, string fileName)
        => folder switch
        {
            UploadFileFolders.CableAttachments => $"{UploadFileFolders.CableAttachments}\\{userId}\\{fileName}",
            UploadFileFolders.CableBanners => $"{UploadFileFolders.CableBanners}\\{userId}\\{fileName}",
            _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, null)
        };


    public static string GetFilePath(string serverUrl,UploadFileFolders folder, string userId, string fileName)
    => folder switch
    {
        UploadFileFolders.CableAttachments => $"{serverUrl}/{UploadFileFolders.CableAttachments}/{userId}/{fileName}",
        UploadFileFolders.CableBanners => $"{serverUrl}/{UploadFileFolders.CableBanners}/{userId}/{fileName}",
        _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, null)
    };
    
    public static void CheckFileIsExist(string filePath)
    {
        if (!File.Exists(filePath))
            throw new NotFoundException(nameof(filePath), filePath);
    }
    
    
}