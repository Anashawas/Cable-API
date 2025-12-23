namespace Infrastructrue.Options;

public class SmsOptions
{
    public const string ConfigName = "SmsService";
    
    public string ApiUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderId { get; set; } = "Cable EV";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
    
}