using Application.Common.Interfaces;
using Application.Common.Models.Emails;
using Infrastructrue.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Infrastructrue.Services;

public class EmailService : IEmailService
{
    private readonly EmailOptions _emailOptions;
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<EmailOptions> emailOptions,
        IEmailTemplateService templateService,
        ILogger<EmailService> logger)
    {
        _emailOptions = emailOptions.Value;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task<EmailResult> SendEmailAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(message.To))
        {
            return new EmailResult
            {
                Success = false,
                Message = "Invalid email address",
                ErrorCode = "INVALID_EMAIL"
            };
        }


        // Validate SMTP configuration
        if (string.IsNullOrEmpty(_emailOptions.SmtpHost) ||
            string.IsNullOrEmpty(_emailOptions.Username))
        {
            _logger.LogError("Email service not configured properly");
            return new EmailResult
            {
                Success = false,
                Message = "Email service not configured",
                ErrorCode = "CONFIG_ERROR"
            };
        }

        // Send with retry logic
        return await SendEmailWithRetryAsync(message, cancellationToken);
    }

    public async Task<EmailResult> SendTemplatedEmailAsync(
        string toEmail,
        string toName,
        EmailTemplate template,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Render template
            var htmlBody = await _templateService.RenderTemplateAsync(
                template.TemplateName,
                template.Variables,
                template.Language);

            var plainTextBody = _templateService.HtmlToPlainText(htmlBody);

            var message = new EmailMessage
            {
                To = toEmail,
                ToName = toName,
                Subject = template.Subject,
                HtmlBody = htmlBody,
                PlainTextBody = plainTextBody
            };

            return await SendEmailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering email template: {TemplateName}", template.TemplateName);
            return new EmailResult
            {
                Success = false,
                Message = $"Template rendering failed: {ex.Message}",
                ErrorCode = "TEMPLATE_ERROR"
            };
        }
    }

    public async Task<BulkEmailResult> SendBulkEmailsAsync(
        List<string> recipients,
        EmailTemplate template,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkEmailResult
        {
            TotalCount = recipients.Count
        };

        foreach (var email in recipients)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var sendResult = await SendTemplatedEmailAsync(
                email,
                null,
                template,
                cancellationToken);

            if (sendResult.Success)
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
                result.FailedEmails.Add(email);
            }

            // Rate limiting delay (1 second between emails)
            await Task.Delay(1000, cancellationToken);
        }

        return result;
    }

    public bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new MailboxAddress("", email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<EmailResult> SendEmailWithRetryAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        var maxAttempts = _emailOptions.MaxRetryAttempts + 1;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await SendEmailViaSmtpAsync(message, cancellationToken);

                _logger.LogInformation(
                    "✅ Email sent successfully to {To} on attempt {Attempt}",
                    message.To,
                    attempt);

                return new EmailResult
                {
                    Success = true,
                    Message = "Email sent successfully",
                    AttemptCount = attempt,
                    SentAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "⚠️ Email send attempt {Attempt} failed for {To}, retrying...",
                    attempt,
                    message.To);

                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        _logger.LogError(
            lastException,
            "❌ Failed to send email to {To} after {Attempts} attempts",
            message.To,
            maxAttempts);

        return new EmailResult
        {
            Success = false,
            Message = lastException?.Message ?? "Unknown error",
            ErrorCode = "SMTP_ERROR",
            AttemptCount = maxAttempts
        };
    }

    private async Task SendEmailViaSmtpAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        var mimeMessage = new MimeMessage();

        // From
        mimeMessage.From.Add(new MailboxAddress(
            _emailOptions.FromName,
            _emailOptions.FromEmail));

        // To
        mimeMessage.To.Add(new MailboxAddress(
            message.ToName ?? message.To,
            message.To));

        // Subject
        mimeMessage.Subject = message.Subject;

        // Body
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody
        };

        // Plain text alternative
        if (!string.IsNullOrEmpty(message.PlainTextBody))
        {
            bodyBuilder.TextBody = message.PlainTextBody;
        }

        // Attachments
        foreach (var attachment in message.Attachments)
        {
            bodyBuilder.Attachments.Add(
                attachment.FileName,
                attachment.Content,
                ContentType.Parse(attachment.ContentType));
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        // Reply-To
        if (!string.IsNullOrEmpty(message.ReplyTo))
        {
            mimeMessage.ReplyTo.Add(new MailboxAddress("", message.ReplyTo));
        }

        // Custom headers
        foreach (var header in message.Headers)
        {
            mimeMessage.Headers.Add(header.Key, header.Value);
        }

        // Send via SMTP using MailKit
        using var smtpClient = new SmtpClient();

        // Set timeout
        smtpClient.Timeout = _emailOptions.TimeoutSeconds * 1000;

        // Connect to SMTP server
        await smtpClient.ConnectAsync(
            _emailOptions.SmtpHost,
            _emailOptions.SmtpPort,
            _emailOptions.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        // Authenticate
        await smtpClient.AuthenticateAsync(
            _emailOptions.Username,
            _emailOptions.Password,
            cancellationToken);

        // Send message
        await smtpClient.SendAsync(mimeMessage, cancellationToken);

        // Disconnect
        await smtpClient.DisconnectAsync(true, cancellationToken);
    }
}
