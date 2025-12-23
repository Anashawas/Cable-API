namespace Infrastructrue.Options;

public class SharedLinkOptions
{
    public const string ConfigName = "SharedLink";
    public string PagePath { get; set; } = null!;
    public string AndroidPackage { get; set; } = "com.cable.projectx";
    public string IosAppId { get; set; } = "6480162073";
    public string WebFallbackUrl { get; set; } = null!;
}