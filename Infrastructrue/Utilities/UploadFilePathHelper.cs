using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using static System.Enum;

namespace Infrastructrue.Utilities;

public static class UploadFilePathHelper
{
    public static string SetupFolderPath(string fileUploadPath, UploadFileFolders folder, string fileName)
    {
        // var isPublicFolder = IsPublicFolder(folder);

        var folderName = folder.ToString();

        var fullPath =
            Path.Combine(fileUploadPath, folderName);

        CreatePath(fullPath);

        return Path.Combine(fullPath, fileName);
    }


    private static void CreatePath(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    // public static bool IsPublicFolder(UploadFileFolders folder)
    // {
    //     var allowedFolders = GetNames<AllowedUploadFiles>().Select(f => f.ToLower());
    //     return allowedFolders.Contains(folder.ToString().ToLower());
    // }

    public static string GetFilePath(string serverUrl, UploadFileFolders folder, string fileName)
    {
        // var isPublicFolder = IsPublicFolder(folder);
        var folderName = folder.ToString();

        return
            $"{serverUrl}/{folderName}/{fileName}";
    }


    public static void CheckFileIsExist(string filePath)
    {
        if (!File.Exists(filePath))
            throw new NotFoundException(nameof(filePath), filePath);
    }
}