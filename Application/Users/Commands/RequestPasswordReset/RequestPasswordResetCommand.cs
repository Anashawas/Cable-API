using Application.Common.Models.Emails;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.RequestPasswordReset;

public record RequestPasswordResetCommand(string Email) : IRequest<RequestPasswordResetDto>;

public class RequestPasswordResetCommandHandler : IRequestHandler<RequestPasswordResetCommand, RequestPasswordResetDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestPasswordResetCommandHandler(
        IApplicationDbContext context,
        IEmailService emailService,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<RequestPasswordResetDto> Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        // Find user by email
        var user = await _context.UserAccounts
            .FirstOrDefaultAsync(x => x.Email == request.Email && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User not found");
        }


        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var recentRequests = await _context.PasswordResets
            .Where(x => x.UserId == user.Id && x.CreatedAt >= oneHourAgo)
            .CountAsync(cancellationToken);

        if (recentRequests >= 3)
        {
            throw new DataValidationException("Max-Attempt", "Too many password reset requests. Please try again later.");
        }

        // Generate 6-digit code
        var random = new Random();
        var resetCode = random.Next(100000, 999999).ToString();

        // Create password reset record
        var passwordReset = new Domain.Enitites.PasswordReset
        {
            UserId = user.Id,
            Code = resetCode,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
            FailedAttempts = 0
        };

        _context.PasswordResets.Add(passwordReset);
        await _context.SaveChanges(cancellationToken);

        // Detect user language from Accept-Language header
        var language = GetUserLanguage();

        // Send email with reset code directly using email service
        var subject = language == "ar"
            ? "إعادة تعيين كلمة مرور Cable EV"
            : "Reset Your Cable EV Password";

        var template = new EmailTemplate
        {
            TemplateName = "password-reset",
            Subject = subject,
            Language = language,
            Variables = new Dictionary<string, object>
            {
                { "userName", user.Name ?? (language == "ar" ? "المستخدم" : "User") },
                { "resetCode", resetCode }
            }
        };

        var emailResult = await _emailService.SendTemplatedEmailAsync(
            user.Email!,
            user.Name ?? "User",
            template,
            cancellationToken);

        if (!emailResult.Success)
        {
            throw new CableApplicationException("Failed to send password reset email");
        }

        return new RequestPasswordResetDto(
            true,
            "Password reset code sent to your email",
            DateTime.UtcNow.AddHours(1));
    }

    private string GetUserLanguage()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return "ar"; // Default to Arabic

        // Get Accept-Language header
        var acceptLanguage = httpContext.Request.Headers["Accept-Language"].FirstOrDefault();

        if (string.IsNullOrEmpty(acceptLanguage))
            return "ar"; // Default to Arabic

        // Parse Accept-Language header (e.g., "en-US,en;q=0.9,ar;q=0.8")
        var languages = acceptLanguage.Split(',')
            .Select(lang => lang.Split(';')[0].Trim().ToLower())
            .ToList();

        // Check for Arabic variants first
        if (languages.Any(lang => lang.StartsWith("ar")))
            return "ar";

        // Check for English variants
        if (languages.Any(lang => lang.StartsWith("en")))
            return "en";

        // Default to Arabic for Middle Eastern market
        return "ar";
    }
}
