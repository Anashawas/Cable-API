using Application.Common.Models.Emails;

namespace Application.Common.Interfaces;

/// <summary>
/// Email service for sending transactional and marketing emails
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send single email message
    /// </summary>
    Task<EmailResult> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send email using template
    /// </summary>
    Task<EmailResult> SendTemplatedEmailAsync(
        string toEmail,
        string toName,
        EmailTemplate template,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send bulk emails (with rate limiting)
    /// </summary>
    Task<BulkEmailResult> SendBulkEmailsAsync(
        List<string> recipients,
        EmailTemplate template,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate email address format
    /// </summary>
    bool IsValidEmail(string email);
}

public class BulkEmailResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> FailedEmails { get; set; } = new();
}
