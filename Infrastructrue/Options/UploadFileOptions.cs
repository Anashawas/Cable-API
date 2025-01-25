namespace Infrastructrue.Options;

public class UploadFileOptions
{
    public const string ConfigName = "File";
    public long LimitSize { get; set; }
    public string[] AllowedExtensions { get; set; } = null!;

    public string ChargingPointAttachmentsPath { get; set; } = null!;
    public string BannerPath { get; set; } = null!;
}