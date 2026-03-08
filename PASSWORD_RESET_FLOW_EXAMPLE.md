# Password Reset Flow - Mobile App (OTP Code)

## 📋 Overview

This document explains how to implement the complete password reset flow for the Cable EV mobile app using **email OTP codes**.

---

## 🔄 Complete Password Reset Flow

### **Step 1: User Requests Password Reset**

Mobile app sends email address to API.

**API Endpoint:** `POST /api/users/request-password-reset`

**Request:**
```json
{
  "email": "user@example.com"
}
```

### **Step 2: Backend Generates 6-Digit Code**

Backend generates a random 6-digit code and stores it in the database.

**Implementation Example:**

```csharp
// File: Application/Users/Commands/RequestPasswordReset/RequestPasswordResetCommand.cs

using Application.Common.Interfaces;
using Application.Emails.Commands.SendPasswordResetEmail;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.RequestPasswordReset;

public record RequestPasswordResetCommand(string Email) : IRequest<RequestPasswordResetResult>;

public class RequestPasswordResetCommandHandler(
    IApplicationDbContext context,
    IMediator mediator)
    : IRequestHandler<RequestPasswordResetCommand, RequestPasswordResetResult>
{
    public async Task<RequestPasswordResetResult> Handle(
        RequestPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        // Find user by email
        var user = await context.UserAccounts
            .FirstOrDefaultAsync(x => x.Email == request.Email && !x.IsDeleted, cancellationToken);

        // Security: Always return success even if email doesn't exist (prevent email enumeration)
        if (user == null)
        {
            return new RequestPasswordResetResult
            {
                Success = true,
                Message = "If an account with that email exists, a password reset code has been sent."
            };
        }

        // Generate 6-digit code
        var resetCode = new Random().Next(100000, 999999).ToString();

        // Store reset code in database with expiry (1 hour)
        var passwordReset = new PasswordReset
        {
            UserId = user.Id,
            ResetCode = resetCode, // TODO: Consider encrypting this
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        context.PasswordResets.Add(passwordReset);
        await context.SaveChanges(cancellationToken);

        // Send email with reset code
        await mediator.Send(new SendPasswordResetEmailCommand(
            user.Email,
            user.Name,
            resetCode
        ), cancellationToken);

        return new RequestPasswordResetResult
        {
            Success = true,
            Message = "Password reset code has been sent to your email."
        };
    }
}

public record RequestPasswordResetResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

### **Step 3: User Receives Email with Code**

User receives email with big 6-digit code:

```
Your Reset Code
  123456
```

### **Step 4: User Enters Code in Mobile App**

Mobile app shows screen:
- Email input (pre-filled)
- 6-digit code input
- New password input
- Confirm password input

### **Step 5: Backend Verifies Code and Resets Password**

**API Endpoint:** `POST /api/users/reset-password-with-code`

**Request:**
```json
{
  "email": "user@example.com",
  "resetCode": "123456",
  "newPassword": "NewSecurePass123!"
}
```

**Implementation Example:**

```csharp
// File: Application/Users/Commands/ResetPasswordWithCode/ResetPasswordWithCodeCommand.cs

using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.ResetPasswordWithCode;

public record ResetPasswordWithCodeCommand(
    string Email,
    string ResetCode,
    string NewPassword
) : IRequest<ResetPasswordResult>;

public class ResetPasswordWithCodeCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher)
    : IRequestHandler<ResetPasswordWithCodeCommand, ResetPasswordResult>
{
    public async Task<ResetPasswordResult> Handle(
        ResetPasswordWithCodeCommand request,
        CancellationToken cancellationToken)
    {
        // Find user
        var user = await context.UserAccounts
            .FirstOrDefaultAsync(x => x.Email == request.Email && !x.IsDeleted, cancellationToken);

        if (user == null)
            throw new NotFoundException("User not found");

        // Find valid reset code
        var passwordReset = await context.PasswordResets
            .Where(x => x.UserId == user.Id &&
                       x.ResetCode == request.ResetCode &&
                       !x.IsUsed &&
                       x.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (passwordReset == null)
            throw new DataValidationException("ResetCode", "Invalid or expired reset code");

        // Mark code as used
        passwordReset.IsUsed = true;

        // Hash new password
        var hashedPassword = passwordHasher.HashPassword(request.NewPassword);

        // Update user password
        user.Password = hashedPassword;

        await context.SaveChanges(cancellationToken);

        return new ResetPasswordResult
        {
            Success = true,
            Message = "Password reset successfully"
        };
    }
}

public record ResetPasswordResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

**Validator:**

```csharp
// File: Application/Users/Commands/ResetPasswordWithCode/ResetPasswordWithCodeCommandValidator.cs

using FluentValidation;

namespace Application.Users.Commands.ResetPasswordWithCode;

public class ResetPasswordWithCodeCommandValidator : AbstractValidator<ResetPasswordWithCodeCommand>
{
    public ResetPasswordWithCodeCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .WithMessage("Valid email is required");

        RuleFor(x => x.ResetCode)
            .NotEmpty()
            .Length(6)
            .Matches(@"^\d{6}$")
            .WithMessage("Reset code must be 6 digits");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]")
            .WithMessage("Password must contain at least one uppercase letter")
            .Matches(@"[a-z]")
            .WithMessage("Password must contain at least one lowercase letter")
            .Matches(@"[0-9]")
            .WithMessage("Password must contain at least one number")
            .Matches(@"[\!\@\#\$\%\^\&\*\(\)\_\+\-\=\[\]\{\}\;\:\'\,\.\<\>\?\/]")
            .WithMessage("Password must contain at least one special character");
    }
}
```

---

## 🗄️ Database Table for Password Reset

You'll need a new table to store reset codes:

**SQL Script:**

```sql
CREATE TABLE [dbo].[PasswordReset]
(
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [ResetCode] NVARCHAR(6) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [IsUsed] BIT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_PasswordReset] PRIMARY KEY CLUSTERED ([Id] ASC),

    CONSTRAINT [FK_PasswordReset_UserAccount]
        FOREIGN KEY ([UserId])
        REFERENCES [dbo].[UserAccount] ([Id])
        ON DELETE CASCADE
);

-- Index for performance
CREATE NONCLUSTERED INDEX [IX_PasswordReset_UserId_ExpiresAt]
ON [dbo].[PasswordReset] ([UserId], [ExpiresAt] DESC)
WHERE [IsUsed] = 0;

-- Index for code lookup
CREATE NONCLUSTERED INDEX [IX_PasswordReset_ResetCode]
ON [dbo].[PasswordReset] ([ResetCode])
WHERE [IsUsed] = 0;

GO
```

**Entity Class:**

```csharp
// File: Domain/Enitites/PasswordReset.cs

namespace Domain.Enitites;

public partial class PasswordReset
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ResetCode { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation property
    public virtual UserAccount UserAccount { get; set; } = null!;
}
```

**Entity Configuration:**

```csharp
// File: Infrastructrue/Persistence/Configurations/PasswordResetConfiguration.cs

using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PasswordResetConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> builder)
    {
        builder.ToTable("PasswordReset");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ResetCode)
            .IsRequired()
            .HasMaxLength(6);

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        builder.Property(x => x.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        // Relationship
        builder.HasOne(x => x.UserAccount)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("FK_PasswordReset_UserAccount")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt })
            .HasDatabaseName("IX_PasswordReset_UserId_ExpiresAt")
            .HasFilter("[IsUsed] = 0");

        builder.HasIndex(x => x.ResetCode)
            .HasDatabaseName("IX_PasswordReset_ResetCode")
            .HasFilter("[IsUsed] = 0");
    }
}
```

---

## 🔒 Security Best Practices

### ✅ **Implemented:**

1. **Code Expiry** - Codes expire after 1 hour
2. **One-Time Use** - Codes can only be used once (IsUsed flag)
3. **Email Enumeration Prevention** - Same response whether email exists or not
4. **Strong Password Requirements** - Enforced in validator

### 🔐 **Recommended Enhancements:**

1. **Encrypt Reset Codes** - Encrypt codes before storing in database
2. **Rate Limiting** - Limit password reset requests per email (e.g., max 3 per hour)
3. **IP Tracking** - Log IP addresses for security auditing
4. **Cleanup Job** - Delete expired codes older than 24 hours

**Rate Limiting Example:**

```csharp
// Check recent reset requests
var recentRequests = await context.PasswordResets
    .Where(x => x.UserId == user.Id &&
               x.CreatedAt > DateTime.UtcNow.AddHours(-1))
    .CountAsync(cancellationToken);

if (recentRequests >= 3)
{
    throw new DataValidationException("Email", "Too many password reset requests. Please try again later.");
}
```

---

## 📱 Mobile App Flow

### **UI Screens:**

1. **Forgot Password Screen**
   - Email input
   - "Send Reset Code" button

2. **Enter Code Screen**
   - 6-digit code input (PIN style)
   - "Verify Code" button
   - "Resend Code" link

3. **New Password Screen**
   - New password input
   - Confirm password input
   - "Reset Password" button

4. **Success Screen**
   - "Password reset successfully!"
   - "Login" button

---

## 🧪 Testing Checklist

- [ ] Email is sent with correct 6-digit code
- [ ] Code expires after 1 hour
- [ ] Code can only be used once
- [ ] Invalid code shows error
- [ ] Expired code shows error
- [ ] Password is successfully updated
- [ ] Strong password requirements enforced
- [ ] Rate limiting works (max 3 requests/hour)
- [ ] Email enumeration is prevented
- [ ] Development mode simulates email (EnableEmail: false)

---

## 🚀 Quick Start

1. **Run SQL script** to create `PasswordReset` table
2. **Add DbSet** to `IApplicationDbContext` and `ApplicationDbContext`
3. **Create commands**: `RequestPasswordResetCommand` and `ResetPasswordWithCodeCommand`
4. **Create API routes** (we'll do this next as per your request to do CORS/API last)
5. **Test with Development mode** (emails are logged, not sent)
6. **Configure SmarterASP SMTP** in Production
7. **Deploy and test** with real emails

---

**Ready to create the API routes?** Let me know when you want to proceed! 🚀
