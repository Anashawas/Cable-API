namespace Infrastructrue.Options;

public class UploadFileOptions
{
    public const string ConfigName = "File";
    public long LimitSize { get; set; }
    public string[] AllowedExtensions { get; set; } = null!;
    public string ServerUrl { get; set; } = null!;
    public string FileUploadPath { get; set; } = null!;
}