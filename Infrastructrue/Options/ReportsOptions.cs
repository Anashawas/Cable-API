namespace Infrastructrue.Options;

public  class ReportsOptions
{
    public const string ConfigName = "Reports";

    public string Path { get; set; } = null!;
    public string BaseUrl  { get; set; } = null!;

}