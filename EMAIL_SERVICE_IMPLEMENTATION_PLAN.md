# Email Service - Complete Implementation Plan
**Cable EV Charging Station Management System**

## 📋 Overview

This document provides a complete implementation plan for adding email functionality to the Cable project using SmarterASP SMTP service. The implementation follows Clean Architecture + CQRS patterns consistent with the existing codebase.

---

## 🎯 Requirements

### Email Capabilities Needed:
1. **Transactional Emails** - Account verification, password reset, notifications
2. **System Notifications** - Charging point updates, favorites, complaints
3. **Marketing/Announcements** - Promotional offers, system announcements
4. **HTML Templates** - Rich email templates with branding
5. **Plain Text Fallback** - For email clients without HTML support
6. **Attachment Support** - PDFs, images, receipts (future)
7. **Queue Support** - Background email sending (optional)

### SmarterASP Email Service:
- **SMTP Server**: mail.yourdomain.com (or smarterasp.net provided server)
- **Port**: 587 (TLS) or 465 (SSL) or 25
- **Authentication**: Username + Password
- **Daily Limit**: Check your SmarterASP plan limits
- **SSL/TLS**: Required for secure connection

---

## 🏗️ Architecture Design

### Design Principles:
✅ **Follows existing patterns** - Same structure as SMS and Notification services
✅ **Clean Architecture** - Clear separation of concerns
✅ **CQRS Compatible** - Email sending via commands
✅ **Template-based** - Reusable email templates
✅ **Async/Await** - Non-blocking operations
✅ **Error Handling** - Retry logic and graceful failures
✅ **Development Mode** - Simulated sending for testing
✅ **Localization** - Arabic and English email templates

### Layer Organization:

```
📁 Cable Project
├── 📁 Application/
│   ├── 📁 Common/Interfaces/
│   │   ├── IEmailService.cs                    # Email service interface
│   │   └── IEmailTemplateService.cs            # Template rendering interface
│   ├── 📁 Emails/
│   │   ├── 📁 Commands/
│   │   │   ├── 📁 SendVerificationEmail/
│   │   │   │   ├── SendVerificationEmailCommand.cs
│   │   │   │   └── SendVerificationEmailCommandValidator.cs
│   │   │   ├── 📁 SendPasswordResetEmail/
│   │   │   │   ├── SendPasswordResetEmailCommand.cs
│   │   │   │   └── SendPasswordResetEmailCommandValidator.cs
│   │   │   ├── 📁 SendNotificationEmail/
│   │   │   │   ├── SendNotificationEmailCommand.cs
│   │   │   │   └── SendNotificationEmailCommandValidator.cs
│   │   │   └── 📁 SendBulkEmail/
│   │   │       ├── SendBulkEmailCommand.cs
│   │   │       └── SendBulkEmailCommandValidator.cs
│   │   └── 📁 Models/
│   │       ├── EmailMessage.cs                 # Email DTO
│   │       ├── EmailResult.cs                  # Send result
│   │       └── EmailTemplate.cs                # Template model
│   └── 📁 Users/Commands/
│       └── RequestPasswordReset/               # Integration example
│
├── 📁 Infrastructrue/
│   ├── 📁 Services/
│   │   ├── EmailService.cs                     # SMTP implementation
│   │   └── EmailTemplateService.cs             # Razor/Scriban templates
│   ├── 📁 Options/
│   │   └── EmailOptions.cs                     # Configuration
│   └── 📁 Templates/
│       ├── 📁 Emails/
│       │   ├── 📁 en/
│       │   │   ├── verification-email.html
│       │   │   ├── password-reset.html
│       │   │   ├── notification.html
│       │   │   └── announcement.html
│       │   └── 📁 ar/
│       │       ├── verification-email.html
│       │       ├── password-reset.html
│       │       ├── notification.html
│       │       └── announcement.html
│       └── 📁 Layouts/
│           ├── base-layout-en.html
│           └── base-layout-ar.html
│
└── 📁 WebApi/
    ├── appsettings.json                        # Email config
    ├── appsettings.Development.json            # Dev config
    └── appsettings.Production.json             # Prod config
```

---

## 📄 Implementation Steps

### Phase 1: Core Infrastructure (Foundation)
### Phase 2: Email Templates (Design)
### Phase 3: Email Commands (Business Logic)
### Phase 4: Integration (Connect to Features)
### Phase 5: Testing & Deployment

---

## 📦 Phase 1: Core Infrastructure

### Step 1.1: Email Options Configuration

**File**: `/Infrastructrue/Options/EmailOptions.cs`

```csharp
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

    // Sending Configuration
    public bool EnableEmail { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 2;

    // Template Configuration
    public string TemplateBasePath { get; set; } = "Templates/Emails";

    // Rate Limiting (optional)
    public int MaxEmailsPerHour { get; set; } = 100;

    // Links Configuration
    public string WebsiteUrl { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
}
```

### Step 1.2: Email Models

**File**: `/Application/Emails/Models/EmailMessage.cs`

```csharp
namespace Application.Emails.Models;

public class EmailMessage
{
    public string To { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string? PlainTextBody { get; set; }
    public string? ReplyTo { get; set; }
    public List<EmailAttachment> Attachments { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class EmailAttachment
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}
```

**File**: `/Application/Emails/Models/EmailResult.cs`

```csharp
namespace Application.Emails.Models;

public class EmailResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public int AttemptCount { get; set; }
    public DateTime SentAt { get; set; }
}
```

**File**: `/Application/Emails/Models/EmailTemplate.cs`

```csharp
namespace Application.Emails.Models;

public class EmailTemplate
{
    public string TemplateName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public Dictionary<string, object> Variables { get; set; } = new();
    public string Language { get; set; } = "en";
}
```

### Step 1.3: Email Service Interface

**File**: `/Application/Common/Interfaces/IEmailService.cs`

```csharp
using Application.Emails.Models;

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
```

**File**: `/Application/Common/Interfaces/IEmailTemplateService.cs`

```csharp
namespace Application.Common.Interfaces;

/// <summary>
/// Service for rendering email templates
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Render HTML template with variables
    /// </summary>
    Task<string> RenderTemplateAsync(
        string templateName,
        Dictionary<string, object> variables,
        string language = "en");

    /// <summary>
    /// Get plain text version of email (strip HTML)
    /// </summary>
    string HtmlToPlainText(string html);
}
```

### Step 1.4: Email Service Implementation

**File**: `/Infrastructrue/Services/EmailService.cs`

```csharp
using System.Net;
using System.Net.Mail;
using Application.Common.Interfaces;
using Application.Emails.Models;
using Infrastructrue.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Services;

public class EmailService : IEmailService
{
    private readonly EmailOptions _emailOptions;
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<EmailService> _logger;
    private static readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);
    private static DateTime _lastEmailSentTime = DateTime.MinValue;

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
        // Validation
        if (!IsValidEmail(message.To))
        {
            return new EmailResult
            {
                Success = false,
                Message = "Invalid email address",
                ErrorCode = "INVALID_EMAIL"
            };
        }

        // Development mode - simulate sending
        if (!_emailOptions.EnableEmail)
        {
            _logger.LogInformation(
                "📧 [DEV MODE] Email simulated: To={To}, Subject={Subject}",
                message.To,
                message.Subject);

            return new EmailResult
            {
                Success = true,
                Message = "Email simulated (development mode)",
                AttemptCount = 1,
                SentAt = DateTime.UtcNow
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
            var addr = new MailAddress(email);
            return addr.Address == email;
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
                    "Email send attempt {Attempt} failed for {To}, retrying...",
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
        using var mailMessage = new MailMessage();

        // From
        mailMessage.From = new MailAddress(
            _emailOptions.FromEmail,
            _emailOptions.FromName);

        // To
        mailMessage.To.Add(new MailAddress(
            message.To,
            message.ToName ?? message.To));

        // Subject and Body
        mailMessage.Subject = message.Subject;
        mailMessage.Body = message.HtmlBody;
        mailMessage.IsBodyHtml = true;

        // Plain text alternative
        if (!string.IsNullOrEmpty(message.PlainTextBody))
        {
            var plainView = AlternateView.CreateAlternateViewFromString(
                message.PlainTextBody,
                null,
                "text/plain");
            mailMessage.AlternateViews.Add(plainView);
        }

        // Reply-To
        if (!string.IsNullOrEmpty(message.ReplyTo))
        {
            mailMessage.ReplyToList.Add(message.ReplyTo);
        }

        // Attachments
        foreach (var attachment in message.Attachments)
        {
            var stream = new MemoryStream(attachment.Content);
            mailMessage.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
        }

        // Custom headers
        foreach (var header in message.Headers)
        {
            mailMessage.Headers.Add(header.Key, header.Value);
        }

        // SMTP client
        using var smtpClient = new SmtpClient(_emailOptions.SmtpHost, _emailOptions.SmtpPort);
        smtpClient.EnableSsl = _emailOptions.EnableSsl;
        smtpClient.UseDefaultCredentials = false;
        smtpClient.Credentials = new NetworkCredential(
            _emailOptions.Username,
            _emailOptions.Password);
        smtpClient.Timeout = _emailOptions.TimeoutSeconds * 1000;

        await smtpClient.SendMailAsync(mailMessage, cancellationToken);
    }
}
```

### Step 1.5: Email Template Service (Simple Version)

**File**: `/Infrastructrue/Services/EmailTemplateService.cs`

```csharp
using System.Text.RegularExpressions;
using Application.Common.Interfaces;
using Infrastructrue.Options;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly EmailOptions _emailOptions;
    private readonly string _templateBasePath;

    public EmailTemplateService(IOptions<EmailOptions> emailOptions)
    {
        _emailOptions = emailOptions.Value;
        _templateBasePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            _emailOptions.TemplateBasePath);
    }

    public async Task<string> RenderTemplateAsync(
        string templateName,
        Dictionary<string, object> variables,
        string language = "en")
    {
        // Load template file
        var templatePath = Path.Combine(_templateBasePath, language, $"{templateName}.html");

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Email template not found: {templatePath}");
        }

        var template = await File.ReadAllTextAsync(templatePath);

        // Simple variable replacement ({{variableName}})
        foreach (var variable in variables)
        {
            var placeholder = $"{{{{{variable.Key}}}}}";
            template = template.Replace(placeholder, variable.Value?.ToString() ?? string.Empty);
        }

        return template;
    }

    public string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove HTML tags
        var plainText = Regex.Replace(html, "<[^>]*>", string.Empty);

        // Decode HTML entities
        plainText = System.Net.WebUtility.HtmlDecode(plainText);

        // Clean up whitespace
        plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

        return plainText;
    }
}
```

---

## 📧 Phase 2: Email Templates

### Base Layout Template

**File**: `/Infrastructrue/Templates/Emails/Layouts/base-layout-en.html`

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{subject}}</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
        }
        .header {
            background-color: #2c3e50;
            color: white;
            padding: 20px;
            text-align: center;
            border-radius: 5px 5px 0 0;
        }
        .content {
            background-color: #f9f9f9;
            padding: 30px;
            border-left: 1px solid #ddd;
            border-right: 1px solid #ddd;
        }
        .button {
            display: inline-block;
            background-color: #3498db;
            color: white;
            padding: 12px 30px;
            text-decoration: none;
            border-radius: 5px;
            margin: 20px 0;
        }
        .footer {
            background-color: #34495e;
            color: #bdc3c7;
            padding: 20px;
            text-align: center;
            font-size: 12px;
            border-radius: 0 0 5px 5px;
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>⚡ Cable EV</h1>
        <p>Electric Vehicle Charging Network</p>
    </div>

    <div class="content">
        {{body}}
    </div>

    <div class="footer">
        <p>© 2026 Cable EV Charging Station Management System</p>
        <p>If you have any questions, contact us at <a href="mailto:{{supportEmail}}" style="color: #3498db;">{{supportEmail}}</a></p>
        <p>
            <a href="{{websiteUrl}}" style="color: #3498db;">Visit Website</a> |
            <a href="{{websiteUrl}}/unsubscribe" style="color: #3498db;">Unsubscribe</a>
        </p>
    </div>
</body>
</html>
```

### Template 1: Verification Email

**File**: `/Infrastructrue/Templates/Emails/en/verification-email.html`

```html
<h2>Welcome to Cable EV! 🎉</h2>

<p>Hi {{userName}},</p>

<p>Thank you for registering with Cable EV, your trusted electric vehicle charging network.</p>

<p>To complete your registration and activate your account, please verify your email address by clicking the button below:</p>

<p style="text-align: center;">
    <a href="{{verificationLink}}" class="button">Verify Email Address</a>
</p>

<p>Or copy and paste this link into your browser:</p>
<p style="background-color: #f0f0f0; padding: 10px; word-break: break-all;">
    {{verificationLink}}
</p>

<p>This verification link will expire in <strong>24 hours</strong>.</p>

<p><strong>Why verify?</strong></p>
<ul>
    <li>✅ Access all charging stations</li>
    <li>✅ Save your favorite locations</li>
    <li>✅ Rate and review charging points</li>
    <li>✅ Receive important notifications</li>
</ul>

<p>If you didn't create an account with Cable EV, please ignore this email.</p>

<p>Best regards,<br>
The Cable EV Team</p>
```

### Template 2: Password Reset

**File**: `/Infrastructrue/Templates/Emails/en/password-reset.html`

```html
<h2>Password Reset Request 🔐</h2>

<p>Hi {{userName}},</p>

<p>We received a request to reset your password for your Cable EV account.</p>

<p>Click the button below to reset your password:</p>

<p style="text-align: center;">
    <a href="{{resetLink}}" class="button">Reset Password</a>
</p>

<p>Or copy and paste this link into your browser:</p>
<p style="background-color: #f0f0f0; padding: 10px; word-break: break-all;">
    {{resetLink}}
</p>

<p>This password reset link will expire in <strong>1 hour</strong>.</p>

<p><strong>⚠️ Security Notice:</strong></p>
<ul>
    <li>If you didn't request this password reset, please ignore this email</li>
    <li>Your password will remain unchanged until you create a new one</li>
    <li>Never share your password or reset link with anyone</li>
</ul>

<p>For security reasons, this link can only be used once.</p>

<p>Best regards,<br>
The Cable EV Team</p>
```

### Template 3: Charging Point Notification

**File**: `/Infrastructrue/Templates/Emails/en/notification.html`

```html
<h2>{{notificationTitle}}</h2>

<p>Hi {{userName}},</p>

<p>{{notificationBody}}</p>

{{#if chargingPointName}}
<div style="background-color: #e8f4f8; padding: 15px; border-radius: 5px; margin: 20px 0;">
    <h3 style="margin-top: 0;">📍 {{chargingPointName}}</h3>
    {{#if chargingPointAddress}}
    <p style="margin: 5px 0;">📌 {{chargingPointAddress}}</p>
    {{/if}}
    {{#if chargingPointStatus}}
    <p style="margin: 5px 0;">⚡ Status: <strong>{{chargingPointStatus}}</strong></p>
    {{/if}}
</div>
{{/if}}

{{#if actionLink}}
<p style="text-align: center;">
    <a href="{{actionLink}}" class="button">View Details</a>
</p>
{{/if}}

<p>Stay charged with Cable EV!</p>

<p>Best regards,<br>
The Cable EV Team</p>
```

### Template 4: System Announcement

**File**: `/Infrastructrue/Templates/Emails/en/announcement.html`

```html
<h2>{{announcementTitle}} 📢</h2>

<p>Hi {{userName}},</p>

<p>{{announcementBody}}</p>

{{#if imageUrl}}
<p style="text-align: center;">
    <img src="{{imageUrl}}" alt="Announcement" style="max-width: 100%; height: auto; border-radius: 5px;">
</p>
{{/if}}

{{#if actionText}}
<p style="text-align: center;">
    <a href="{{actionLink}}" class="button">{{actionText}}</a>
</p>
{{/if}}

<p>Thank you for being part of the Cable EV community!</p>

<p>Best regards,<br>
The Cable EV Team</p>
```

---

## 🎯 Phase 3: Email Commands (CQRS)

### Command 1: Send Verification Email

**File**: `/Application/Emails/Commands/SendVerificationEmail/SendVerificationEmailCommand.cs`

```csharp
using Application.Common.Interfaces;
using Application.Emails.Models;
using MediatR;

namespace Application.Emails.Commands.SendVerificationEmail;

public record SendVerificationEmailCommand(
    string Email,
    string UserName,
    string VerificationToken
) : IRequest<EmailResult>;

public class SendVerificationEmailCommandHandler(
    IEmailService emailService,
    IConfiguration configuration)
    : IRequestHandler<SendVerificationEmailCommand, EmailResult>
{
    public async Task<EmailResult> Handle(
        SendVerificationEmailCommand request,
        CancellationToken cancellationToken)
    {
        var websiteUrl = configuration["EmailService:WebsiteUrl"] ?? "https://cable-ev.com";
        var verificationLink = $"{websiteUrl}/verify-email?token={request.VerificationToken}";

        var template = new EmailTemplate
        {
            TemplateName = "verification-email",
            Subject = "Verify Your Cable EV Email Address",
            Language = "en",
            Variables = new Dictionary<string, object>
            {
                { "userName", request.UserName },
                { "verificationLink", verificationLink }
            }
        };

        return await emailService.SendTemplatedEmailAsync(
            request.Email,
            request.UserName,
            template,
            cancellationToken);
    }
}
```

**File**: `/Application/Emails/Commands/SendVerificationEmail/SendVerificationEmailCommandValidator.cs`

```csharp
using FluentValidation;

namespace Application.Emails.Commands.SendVerificationEmail;

public class SendVerificationEmailCommandValidator : AbstractValidator<SendVerificationEmailCommand>
{
    public SendVerificationEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .WithMessage("Valid email address is required");

        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("User name is required");

        RuleFor(x => x.VerificationToken)
            .NotEmpty()
            .WithMessage("Verification token is required");
    }
}
```

### Command 2: Send Password Reset Email

**File**: `/Application/Emails/Commands/SendPasswordResetEmail/SendPasswordResetEmailCommand.cs`

```csharp
using Application.Common.Interfaces;
using Application.Emails.Models;
using MediatR;

namespace Application.Emails.Commands.SendPasswordResetEmail;

public record SendPasswordResetEmailCommand(
    string Email,
    string UserName,
    string ResetToken
) : IRequest<EmailResult>;

public class SendPasswordResetEmailCommandHandler(
    IEmailService emailService,
    IConfiguration configuration)
    : IRequestHandler<SendPasswordResetEmailCommand, EmailResult>
{
    public async Task<EmailResult> Handle(
        SendPasswordResetEmailCommand request,
        CancellationToken cancellationToken)
    {
        var websiteUrl = configuration["EmailService:WebsiteUrl"] ?? "https://cable-ev.com";
        var resetLink = $"{websiteUrl}/reset-password?token={request.ResetToken}";

        var template = new EmailTemplate
        {
            TemplateName = "password-reset",
            Subject = "Reset Your Cable EV Password",
            Language = "en",
            Variables = new Dictionary<string, object>
            {
                { "userName", request.UserName },
                { "resetLink", resetLink }
            }
        };

        return await emailService.SendTemplatedEmailAsync(
            request.Email,
            request.UserName,
            template,
            cancellationToken);
    }
}
```

**File**: `/Application/Emails/Commands/SendPasswordResetEmail/SendPasswordResetEmailCommandValidator.cs`

```csharp
using FluentValidation;

namespace Application.Emails.Commands.SendPasswordResetEmail;

public class SendPasswordResetEmailCommandValidator : AbstractValidator<SendPasswordResetEmailCommand>
{
    public SendPasswordResetEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .WithMessage("Valid email address is required");

        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("User name is required");

        RuleFor(x => x.ResetToken)
            .NotEmpty()
            .WithMessage("Reset token is required");
    }
}
```

### Command 3: Send Notification Email

**File**: `/Application/Emails/Commands/SendNotificationEmail/SendNotificationEmailCommand.cs`

```csharp
using Application.Common.Interfaces;
using Application.Emails.Models;
using MediatR;

namespace Application.Emails.Commands.SendNotificationEmail;

public record SendNotificationEmailCommand(
    string Email,
    string UserName,
    string NotificationTitle,
    string NotificationBody,
    string? ChargingPointName = null,
    string? ChargingPointAddress = null,
    string? ChargingPointStatus = null,
    string? ActionLink = null
) : IRequest<EmailResult>;

public class SendNotificationEmailCommandHandler(
    IEmailService emailService)
    : IRequestHandler<SendNotificationEmailCommand, EmailResult>
{
    public async Task<EmailResult> Handle(
        SendNotificationEmailCommand request,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object>
        {
            { "userName", request.UserName },
            { "notificationTitle", request.NotificationTitle },
            { "notificationBody", request.NotificationBody }
        };

        if (!string.IsNullOrEmpty(request.ChargingPointName))
            variables["chargingPointName"] = request.ChargingPointName;

        if (!string.IsNullOrEmpty(request.ChargingPointAddress))
            variables["chargingPointAddress"] = request.ChargingPointAddress;

        if (!string.IsNullOrEmpty(request.ChargingPointStatus))
            variables["chargingPointStatus"] = request.ChargingPointStatus;

        if (!string.IsNullOrEmpty(request.ActionLink))
            variables["actionLink"] = request.ActionLink;

        var template = new EmailTemplate
        {
            TemplateName = "notification",
            Subject = request.NotificationTitle,
            Language = "en",
            Variables = variables
        };

        return await emailService.SendTemplatedEmailAsync(
            request.Email,
            request.UserName,
            template,
            cancellationToken);
    }
}
```

---

## ⚙️ Phase 4: Configuration

### Production Configuration (SmarterASP)

**File**: `/WebApi/appsettings.Production.json`

```json
{
  "EmailService": {
    "SmtpHost": "mail.yourdomain.com",
    "SmtpPort": 587,
    "EnableSsl": true,
    "Username": "noreply@yourdomain.com",
    "Password": "your-password-here",
    "FromEmail": "noreply@yourdomain.com",
    "FromName": "Cable EV",
    "EnableEmail": true,
    "TimeoutSeconds": 30,
    "MaxRetryAttempts": 2,
    "TemplateBasePath": "Templates/Emails",
    "MaxEmailsPerHour": 100,
    "WebsiteUrl": "https://yourdomain.com",
    "LogoUrl": "https://yourdomain.com/logo.png",
    "SupportEmail": "support@yourdomain.com"
  }
}
```

### Development Configuration

**File**: `/WebApi/appsettings.Development.json`

```json
{
  "EmailService": {
    "EnableEmail": false,
    "FromName": "Cable EV Dev"
  }
}
```

### Dependency Injection

**File**: `/Infrastructrue/DependencyInjection.cs`

```csharp
// Add this method to register email services
private static IServiceCollection RegisterEmailServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Configure options
    services.Configure<EmailOptions>(
        configuration.GetSection(EmailOptions.ConfigName));

    // Register services
    services.AddScoped<IEmailService, EmailService>();
    services.AddScoped<IEmailTemplateService, EmailTemplateService>();

    return services;
}

// Call in AddInfrastructure method:
public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // ... existing code ...

    services.RegisterEmailServices(configuration);

    return services;
}
```

---

## 🔗 Phase 5: Integration Examples

### Example 1: User Registration

**File**: `/Application/Users/Commands/AddUser/AddUserCommand.cs`

```csharp
// Add to handler after user creation:

// Send verification email
var verificationToken = Guid.NewGuid().ToString();
// TODO: Store verificationToken in database with expiry

await _mediator.Send(new SendVerificationEmailCommand(
    request.Email,
    request.Name,
    verificationToken
), cancellationToken);
```

### Example 2: Password Reset Request

**File**: `/Application/Users/Commands/RequestPasswordReset/RequestPasswordResetCommand.cs`

```csharp
public record RequestPasswordResetCommand(string Email) : IRequest<string>;

public class RequestPasswordResetCommandHandler(
    IApplicationDbContext context,
    IMediator mediator)
    : IRequestHandler<RequestPasswordResetCommand, string>
{
    public async Task<string> Handle(
        RequestPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        var user = await context.UserAccounts
            .FirstOrDefaultAsync(x => x.Email == request.Email && !x.IsDeleted, cancellationToken);

        if (user == null)
            return "If an account with that email exists, a password reset link has been sent.";

        // Generate reset token
        var resetToken = Guid.NewGuid().ToString();
        // TODO: Store resetToken in database with 1-hour expiry

        // Send email
        await mediator.Send(new SendPasswordResetEmailCommand(
            user.Email,
            user.Name,
            resetToken
        ), cancellationToken);

        return "Password reset email sent successfully.";
    }
}
```

### Example 3: Charging Point Status Change

```csharp
// When charging point status changes:

var owner = await context.UserAccounts
    .FirstOrDefaultAsync(x => x.Id == chargingPoint.OwnerId, cancellationToken);

if (owner != null && !string.IsNullOrEmpty(owner.Email))
{
    await mediator.Send(new SendNotificationEmailCommand(
        owner.Email,
        owner.Name,
        "Charging Point Status Updated",
        $"Your charging point '{chargingPoint.Name}' status has been updated.",
        chargingPoint.Name,
        chargingPoint.Address,
        newStatus.Name,
        $"https://cable-ev.com/charging-point/{chargingPoint.Id}"
    ), cancellationToken);
}
```

---

## 🧪 Testing Plan

### Manual Testing Checklist:

1. **Test Email Configuration**
   - ✅ Update appsettings with SmarterASP SMTP details
   - ✅ Test connection from development environment
   - ✅ Verify SSL/TLS settings

2. **Test Development Mode**
   - ✅ Set `EnableEmail: false`
   - ✅ Verify emails are logged but not sent
   - ✅ Check log output format

3. **Test Templates**
   - ✅ Create test endpoint to render templates
   - ✅ Verify all variables are replaced
   - ✅ Check HTML and plain text versions
   - ✅ Test Arabic templates (RTL)

4. **Test Email Sending**
   - ✅ Send verification email
   - ✅ Send password reset email
   - ✅ Send notification email
   - ✅ Test with valid/invalid emails
   - ✅ Verify retry logic on failure

5. **Test Integration**
   - ✅ Register new user → receive verification email
   - ✅ Request password reset → receive reset email
   - ✅ Favorite charging point → owner receives email (if enabled)

### Test Endpoint (Remove in Production)

**File**: `/WebApi/Routes/TestRoutes.cs`

```csharp
#if DEBUG
app.MapPost("/api/test/send-test-email", async (
    IMediator mediator,
    [FromBody] TestEmailRequest request,
    CancellationToken cancellationToken) =>
{
    var result = await mediator.Send(new SendVerificationEmailCommand(
        request.Email,
        "Test User",
        "test-token-12345"
    ), cancellationToken);

    return Results.Ok(result);
})
.WithName("Send Test Email")
.WithTags("Testing");

public record TestEmailRequest(string Email);
#endif
```

---

## 📊 Monitoring & Logging

### Key Metrics to Monitor:

1. **Email Success Rate** - % of emails sent successfully
2. **Delivery Time** - Average time to send email
3. **Bounce Rate** - Invalid email addresses
4. **Retry Rate** - How often retries are needed
5. **Template Errors** - Missing templates or variables
6. **SMTP Errors** - Authentication failures, timeouts

### Logging Examples:

```csharp
_logger.LogInformation("📧 Email sent to {Email}: {Subject}", email, subject);
_logger.LogWarning("⚠️ Email retry attempt {Attempt} for {Email}", attempt, email);
_logger.LogError(ex, "❌ Email sending failed for {Email}", email);
```

---

## 🚀 Deployment Steps

### Pre-Deployment:

1. **Get SmarterASP SMTP Details**
   - Login to SmarterASP control panel
   - Navigate to Email Accounts
   - Note: SMTP host, port, credentials

2. **Create Email Account**
   - Create `noreply@yourdomain.com`
   - Set strong password
   - Configure forwarding if needed

3. **Update Configuration**
   - Update `appsettings.Production.json`
   - Test from development first
   - Deploy to staging

### Deployment:

1. **Deploy Email Templates**
   - Ensure `/Templates/Emails/` folder is included in publish
   - Verify file paths are correct

2. **Update appsettings**
   - Set production SMTP credentials
   - Set `EnableEmail: true`
   - Configure website URLs

3. **Test Production**
   - Send test email from production
   - Verify delivery
   - Check logs for errors

### Post-Deployment:

1. **Monitor Logs** - Watch for SMTP errors
2. **Test All Email Types** - Verification, reset, notifications
3. **Check Spam Folders** - Ensure emails aren't marked as spam
4. **Configure SPF/DKIM** - Improve deliverability (SmarterASP docs)

---

## 📝 Files to Create (Summary)

### Infrastructure (8 files):
1. `/Infrastructrue/Options/EmailOptions.cs`
2. `/Infrastructrue/Services/EmailService.cs`
3. `/Infrastructrue/Services/EmailTemplateService.cs`
4. `/Infrastructrue/Templates/Emails/Layouts/base-layout-en.html`
5. `/Infrastructrue/Templates/Emails/en/verification-email.html`
6. `/Infrastructrue/Templates/Emails/en/password-reset.html`
7. `/Infrastructrue/Templates/Emails/en/notification.html`
8. `/Infrastructrue/Templates/Emails/en/announcement.html`

### Application Layer (12 files):
9. `/Application/Common/Interfaces/IEmailService.cs`
10. `/Application/Common/Interfaces/IEmailTemplateService.cs`
11. `/Application/Emails/Models/EmailMessage.cs`
12. `/Application/Emails/Models/EmailResult.cs`
13. `/Application/Emails/Models/EmailTemplate.cs`
14. `/Application/Emails/Commands/SendVerificationEmail/SendVerificationEmailCommand.cs`
15. `/Application/Emails/Commands/SendVerificationEmail/SendVerificationEmailCommandValidator.cs`
16. `/Application/Emails/Commands/SendPasswordResetEmail/SendPasswordResetEmailCommand.cs`
17. `/Application/Emails/Commands/SendPasswordResetEmail/SendPasswordResetEmailCommandValidator.cs`
18. `/Application/Emails/Commands/SendNotificationEmail/SendNotificationEmailCommand.cs`
19. `/Application/Emails/Commands/SendBulkEmail/SendBulkEmailCommand.cs` (optional)
20. `/Application/Emails/Commands/SendBulkEmail/SendBulkEmailCommandValidator.cs` (optional)

### Configuration (3 files):
21. Update `/WebApi/appsettings.json`
22. Update `/WebApi/appsettings.Development.json`
23. Update `/WebApi/appsettings.Production.json`

### Modifications (1 file):
24. Update `/Infrastructrue/DependencyInjection.cs`

---

## 🎯 Implementation Priorities

### Phase 1 (Essential - Week 1):
✅ Core email service infrastructure
✅ Basic email templates (verification, password reset)
✅ Configuration and DI setup
✅ Testing in development mode

### Phase 2 (Important - Week 2):
✅ Production SMTP configuration
✅ Integration with user registration
✅ Integration with password reset
✅ Arabic email templates

### Phase 3 (Nice to Have - Week 3):
✅ Notification emails for charging point updates
✅ Announcement/marketing emails
✅ Bulk email capability
✅ Email logging and monitoring

---

## ⚠️ Important Notes

1. **SmarterASP Limits** - Check your plan's email sending limits
2. **SPF/DKIM Records** - Configure for better deliverability
3. **Spam Prevention** - Don't send unsolicited emails
4. **GDPR Compliance** - Include unsubscribe links
5. **Rate Limiting** - Implement delays for bulk emails
6. **Template Testing** - Test across email clients (Gmail, Outlook, etc.)
7. **Arabic Templates** - Test RTL layout carefully
8. **Error Handling** - Email failures shouldn't break user flows
9. **Async Operations** - Don't block API responses waiting for emails
10. **Security** - Never log email passwords

---

## 📞 SmarterASP Support

If you encounter issues:
- **Control Panel**: https://www.smarterasp.net/
- **SMTP Documentation**: Check SmarterASP knowledge base
- **Port Options**: Try 587 (TLS), 465 (SSL), or 25
- **Authentication**: Use full email as username

---

## 🎓 Next Steps

After reviewing this plan:
1. ✅ Approve the architecture
2. ✅ Request any modifications
3. ✅ Get SmarterASP SMTP details ready
4. ✅ Start Phase 1 implementation
5. ✅ Test in development mode
6. ✅ Deploy to production

---

**End of Email Service Implementation Plan**

This plan follows your existing architecture patterns and is ready for implementation!
