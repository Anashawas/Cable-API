using Microsoft.AspNetCore.Http;

namespace Application.Common.Extensions;

public static class FileExtensions
{
    public static string GetFileName(this IFormFile file)
        =>
            $"{Path.GetFileNameWithoutExtension(file.FileName)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(file.FileName)}";


    public static string GetFileExtension(this IFormFile file)
        => Path.GetExtension(file.FileName);


    public static async Task<byte[]> GetFileBytesAsync(this IFormFile file)
    {
        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
}