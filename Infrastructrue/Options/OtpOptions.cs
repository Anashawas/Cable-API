namespace Infrastructrue.Options;

public class OtpOptions
{
    public const string ConfigName = "OtpSettings";
    
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;
    public int RateLimitMinutes { get; set; } = 1;
    public int MaxRequestsPerWindow { get; set; } = 1;
}