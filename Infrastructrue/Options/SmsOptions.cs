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

    /// <summary>Below this provider balance /api/health/sms reports a problem, so it can be topped up in time.</summary>
    public decimal MinBalance { get; set; } = 1000;
    
}