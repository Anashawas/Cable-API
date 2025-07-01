using Application.Common.Enums;
using Cable.Core.Exceptions;

namespace Infrastructrue.Utilities;

public static class UploadFilePathHelper
{
    public static string SetupFolderPath(string fileUploadPath, UploadFileFolders folder, string userId, string fileName)
    {
        CreatePath(fileUploadPath,folder,userId);
       return folder switch
        {
            UploadFileFolders.CableAttachments => Path.Combine( fileUploadPath,UploadFileFolders.CableAttachments.ToString(),userId,fileName),
            UploadFileFolders.CableBanners =>  Path.Combine( fileUploadPath,UploadFileFolders.CableBanners.ToString(),userId,fileName),
            _ => throw new NotFoundException(nameof(folder), folder)
        }; 
    } 
    
   

    private static void CreatePath(string fileUploadPath, UploadFileFolders folder, string userId)
    {
        var existPath = Path.Combine(fileUploadPath, folder.ToString(),userId);
        if (!Path.Exists(existPath))
            Directory.CreateDirectory(existPath);
    }

    public static string GetFilePath(string serverUrl, UploadFileFolders folder, string userId, string fileName)
    {
        return folder switch
        {
            UploadFileFolders.CableAttachments =>
                $"{serverUrl}/{UploadFileFolders.CableAttachments}/{userId}/{fileName}",
            UploadFileFolders.CableBanners => $"{serverUrl}/{UploadFileFolders.CableBanners}/{userId}/{fileName}",
            _ => throw new NotFoundException(nameof(folder), folder)
        };
    }


    public static void CheckFileIsExist(string filePath)
    {
        if (!File.Exists(filePath)) 
            throw new NotFoundException(nameof(filePath), filePath);
    }
}