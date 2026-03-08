namespace Infrastructrue.Options;

public class EmailOptions
{
    public const string ConfigName = "EmailService";

    // SMTP Configuration
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Cable EV";


    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 2;

    // Template Configuration
    public string TemplateBasePath { get; set; } = "Templates/Emails";

    // Rate Limiting (optional)
    public int MaxEmailsPerHour { get; set; } = 100;

    // Mobile App Configuration (Optional - for email footers)
    public string? AppName { get; set; } = "Cable EV";
    public string? SupportEmail { get; set; }
    public string? LogoUrl { get; set; }

    // Website URL (Optional - only if you add website later)
    public string? WebsiteUrl { get; set; }
}
