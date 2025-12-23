# Phone Number Login with OTP - Implementation Guide

## Database Changes for Phone Authentication

### Step 1: Update UserAccount Table

Add phone verification columns to your existing `UserAccount` table:

```sql
-- Add phone verification columns to UserAccount table
ALTER TABLE [dbo].[UserAccount] 
ADD 
    [IsPhoneVerified] bit NOT NULL DEFAULT 0,
    [PhoneVerifiedAt] datetime2(7) NULL;

-- Update existing records to set default values
UPDATE [dbo].[UserAccount] 
SET [IsPhoneVerified] = 0 
WHERE [IsPhoneVerified] IS NULL;
```

### Step 2: Create PhoneVerification Table

Create the main table to manage OTP codes and phone verification:

```sql
-- Create PhoneVerification table
CREATE TABLE [dbo].[PhoneVerification] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [PhoneNumber] nvarchar(20) NOT NULL,
    [OtpCode] nvarchar(10) NOT NULL,
    [ExpiresAt] datetime2(7) NOT NULL,
    [IsVerified] bit NOT NULL DEFAULT 0,
    [IsUsed] bit NOT NULL DEFAULT 0,
    [AttemptCount] int NOT NULL DEFAULT 0,
    [UserId] int NULL,
    [CreatedAt] datetime2(7) NULL DEFAULT GETUTCDATE(),
    [CreatedBy] int NULL,
    [ModifiedAt] datetime2(7) NULL,
    [ModifiedBy] int NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    
    CONSTRAINT [PK_PhoneVerification] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PhoneVerification_UserAccount_UserId] FOREIGN KEY ([UserId]) 
        REFERENCES [dbo].[UserAccount] ([Id]) ON DELETE SET NULL
);
```

### Step 3: Add Indexes and Constraints

Add performance and security indexes:

```sql
-- Add indexes for better performance and security
CREATE NONCLUSTERED INDEX [IX_PhoneVerification_PhoneNumber_CreatedAt] 
ON [dbo].[PhoneVerification] ([PhoneNumber] ASC, [CreatedAt] DESC)
WHERE [IsDeleted] = 0;

CREATE NONCLUSTERED INDEX [IX_PhoneVerification_ExpiresAt] 
ON [dbo].[PhoneVerification] ([ExpiresAt] ASC)
WHERE [IsDeleted] = 0 AND [IsUsed] = 0;

CREATE NONCLUSTERED INDEX [IX_PhoneVerification_UserId] 
ON [dbo].[PhoneVerification] ([UserId] ASC)
WHERE [UserId] IS NOT NULL AND [IsDeleted] = 0;

-- Add index on UserAccount for phone number lookups
CREATE NONCLUSTERED INDEX [IX_UserAccount_Phone_IsDeleted] 
ON [dbo].[UserAccount] ([Phone] ASC, [IsDeleted] ASC)
WHERE [Phone] IS NOT NULL;
```

### Step 4: Create Rate Limiting Table (Optional but Recommended)

For better rate limiting control:

```sql
-- Create OtpRateLimit table for advanced rate limiting
CREATE TABLE [dbo].[OtpRateLimit] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [PhoneNumber] nvarchar(20) NOT NULL,
    [RequestCount] int NOT NULL DEFAULT 1,
    [WindowStart] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
    [LastRequestAt] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
    [IsBlocked] bit NOT NULL DEFAULT 0,
    [BlockedUntil] datetime2(7) NULL,
    
    CONSTRAINT [PK_OtpRateLimit] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- Add unique constraint for phone number
CREATE UNIQUE NONCLUSTERED INDEX [IX_OtpRateLimit_PhoneNumber_Unique] 
ON [dbo].[OtpRateLimit] ([PhoneNumber] ASC);

-- Add index for cleanup operations
CREATE NONCLUSTERED INDEX [IX_OtpRateLimit_WindowStart] 
ON [dbo].[OtpRateLimit] ([WindowStart] ASC);
```

### Step 5: Cleanup Job for Expired Records

Add a cleanup procedure for maintenance:

```sql
-- Create stored procedure for cleanup
CREATE PROCEDURE [dbo].[CleanupExpiredOtpRecords]
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Delete expired OTP records older than 24 hours
    DELETE FROM [dbo].[PhoneVerification]
    WHERE [ExpiresAt] < DATEADD(HOUR, -24, GETUTCDATE())
        AND ([IsUsed] = 1 OR [IsVerified] = 1);
    
    -- Reset rate limit records older than 24 hours
    DELETE FROM [dbo].[OtpRateLimit]
    WHERE [WindowStart] < DATEADD(HOUR, -24, GETUTCDATE())
        AND [IsBlocked] = 0;
    
    -- Unblock expired blocks
    UPDATE [dbo].[OtpRateLimit]
    SET [IsBlocked] = 0, [BlockedUntil] = NULL
    WHERE [IsBlocked] = 1 
        AND [BlockedUntil] IS NOT NULL 
        AND [BlockedUntil] < GETUTCDATE();
END;
```

### Complete SQL Script to Run

Here's the complete script you can run in SQL Server Management Studio:

```sql
-- Complete Phone Authentication Database Setup Script
BEGIN TRANSACTION;

BEGIN TRY
    -- Step 1: Update UserAccount table
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('[dbo].[UserAccount]') AND name = 'IsPhoneVerified')
    BEGIN
        ALTER TABLE [dbo].[UserAccount] ADD [IsPhoneVerified] bit NOT NULL DEFAULT 0;
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('[dbo].[UserAccount]') AND name = 'PhoneVerifiedAt')
    BEGIN
        ALTER TABLE [dbo].[UserAccount] ADD [PhoneVerifiedAt] datetime2(7) NULL;
    END

    -- Step 2: Create PhoneVerification table
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('[dbo].[PhoneVerification]'))
    BEGIN
        CREATE TABLE [dbo].[PhoneVerification] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [PhoneNumber] nvarchar(20) NOT NULL,
            [OtpCode] nvarchar(10) NOT NULL,
            [ExpiresAt] datetime2(7) NOT NULL,
            [IsVerified] bit NOT NULL DEFAULT 0,
            [IsUsed] bit NOT NULL DEFAULT 0,
            [AttemptCount] int NOT NULL DEFAULT 0,
            [UserId] int NULL,
            [CreatedAt] datetime2(7) NULL DEFAULT GETUTCDATE(),
            [CreatedBy] int NULL,
            [ModifiedAt] datetime2(7) NULL,
            [ModifiedBy] int NULL,
            [IsDeleted] bit NOT NULL DEFAULT 0,
            
            CONSTRAINT [PK_PhoneVerification] PRIMARY KEY CLUSTERED ([Id] ASC),
            CONSTRAINT [FK_PhoneVerification_UserAccount_UserId] FOREIGN KEY ([UserId]) 
                REFERENCES [dbo].[UserAccount] ([Id]) ON DELETE SET NULL
        );
    END

    -- Step 3: Create OtpRateLimit table
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('[dbo].[OtpRateLimit]'))
    BEGIN
        CREATE TABLE [dbo].[OtpRateLimit] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [PhoneNumber] nvarchar(20) NOT NULL,
            [RequestCount] int NOT NULL DEFAULT 1,
            [WindowStart] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
            [LastRequestAt] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
            [IsBlocked] bit NOT NULL DEFAULT 0,
            [BlockedUntil] datetime2(7) NULL,
            
            CONSTRAINT [PK_OtpRateLimit] PRIMARY KEY CLUSTERED ([Id] ASC)
        );
    END

    -- Step 4: Create indexes
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PhoneVerification_PhoneNumber_CreatedAt')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_PhoneVerification_PhoneNumber_CreatedAt] 
        ON [dbo].[PhoneVerification] ([PhoneNumber] ASC, [CreatedAt] DESC)
        WHERE [IsDeleted] = 0;
    END

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PhoneVerification_ExpiresAt')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_PhoneVerification_ExpiresAt] 
        ON [dbo].[PhoneVerification] ([ExpiresAt] ASC)
        WHERE [IsDeleted] = 0 AND [IsUsed] = 0;
    END

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OtpRateLimit_PhoneNumber_Unique')
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX [IX_OtpRateLimit_PhoneNumber_Unique] 
        ON [dbo].[OtpRateLimit] ([PhoneNumber] ASC);
    END

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserAccount_Phone_IsDeleted')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_UserAccount_Phone_IsDeleted] 
        ON [dbo].[UserAccount] ([Phone] ASC, [IsDeleted] ASC)
        WHERE [Phone] IS NOT NULL;
    END

    COMMIT TRANSACTION;
    PRINT 'Phone authentication database setup completed successfully.';
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'Error occurred: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

## After Running SQL Scripts - Reverse Engineering

### 1. Update Your Connection String
Make sure your connection string in `appsettings.json` points to the updated database.

### 2. Reverse Engineer Command
Use Entity Framework tools to reverse engineer:

```bash
# Navigate to your project root
cd /path/to/your/Cable/project

# Reverse engineer (update existing context)
dotnet ef dbcontext scaffold "YourConnectionString" Microsoft.EntityFrameworkCore.SqlServer ^
  --context-dir Infrastructrue/Persistence ^
  --output-dir Domain/Enitites ^
  --context ApplicationDbContext ^
  --force ^
  --no-build ^
  --project Infrastructrue ^
  --startup-project WebApi
```

### 3. Manually Verify Generated Entities

After reverse engineering, check these files:
- `Domain/Enitites/PhoneVerification.cs` 
- `Domain/Enitites/OtpRateLimit.cs`
- `Domain/Enitites/UserAccount.cs` (updated)
- `Infrastructrue/Persistence/ApplicationDbContext.cs` (updated)

---

## Next Steps (Application Layer Implementation)

### Step 1: Application Layer - Interfaces

#### 1.1 Create OTP Service Interface
```csharp
// Application/Common/Interfaces/IOtpService.cs
public interface IOtpService
{
    Task<string> GenerateOtpAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken);
    Task<bool> VerifyOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken);
    Task<bool> IsRateLimitedAsync(string phoneNumber, CancellationToken cancellationToken);
}
```

#### 1.2 Update Authentication Service Interface
```csharp
// Add to Application/Common/Interfaces/IAuthenticationService.cs
Task<string> SendOtpAsync(string phoneNumber, CancellationToken cancellationToken);
Task<UserLoginResult> LoginWithOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken);
```

### Step 2: Application Layer - Commands & Queries

#### 2.1 Send OTP Command
```csharp
// Application/Authentication/Commands/SendOtp/SendOtpCommand.cs
public record SendOtpCommand(string PhoneNumber) : IRequest<SendOtpResult>;

public class SendOtpCommandHandler : IRequestHandler<SendOtpCommand, SendOtpResult>
{
    private readonly IOtpService _otpService;
    private readonly IApplicationDbContext _context;

    public SendOtpCommandHandler(IOtpService otpService, IApplicationDbContext context)
    {
        _otpService = otpService;
        _context = context;
    }

    public async Task<SendOtpResult> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        // Check rate limiting
        if (await _otpService.IsRateLimitedAsync(request.PhoneNumber, cancellationToken))
        {
            return new SendOtpResult(false, "Rate limit exceeded. Please try again later.", null);
        }

        // Generate and send OTP
        var otp = await _otpService.GenerateOtpAsync(request.PhoneNumber, cancellationToken);
        var sent = await _otpService.SendOtpAsync(request.PhoneNumber, otp, cancellationToken);

        if (sent)
        {
            return new SendOtpResult(true, "OTP sent successfully", DateTime.UtcNow.AddMinutes(5));
        }

        return new SendOtpResult(false, "Failed to send OTP", null);
    }
}

// Application/Authentication/Commands/SendOtp/SendOtpCommandValidator.cs
public class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+965[0-9]{8}$")
            .WithMessage("Phone number must be a valid Kuwait number (+965XXXXXXXX)");
    }
}

// Application/Authentication/Commands/SendOtp/SendOtpResult.cs
public record SendOtpResult(bool Success, string Message, DateTime? ExpiresAt);
```

#### 2.2 Verify OTP Command
```csharp
// Application/Authentication/Commands/VerifyOtp/VerifyOtpCommand.cs
public record VerifyOtpCommand(string PhoneNumber, string OtpCode) : IRequest<UserLoginResult>;

public class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, UserLoginResult>
{
    private readonly IOtpService _otpService;
    private readonly IAuthenticationService _authenticationService;

    public VerifyOtpCommandHandler(IOtpService otpService, IAuthenticationService authenticationService)
    {
        _otpService = otpService;
        _authenticationService = authenticationService;
    }

    public async Task<UserLoginResult> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        return await _authenticationService.LoginWithOtpAsync(request.PhoneNumber, request.OtpCode, cancellationToken);
    }
}

// Application/Authentication/Commands/VerifyOtp/VerifyOtpCommandValidator.cs
public class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+965[0-9]{8}$");

        RuleFor(x => x.OtpCode)
            .NotEmpty()
            .Length(6)
            .Matches(@"^\d{6}$")
            .WithMessage("OTP must be 6 digits");
    }
}
```

### Step 3: Infrastructure Layer - OTP Service

#### 3.1 OTP Service Implementation
```csharp
// Infrastructure/Services/OtpService.cs
public class OtpService : IOtpService
{
    private readonly IApplicationDbContext _context;
    private readonly ISmsService _smsService;
    private readonly IDataEncryption _dataEncryption;
    private readonly IOptions<OtpOptions> _otpOptions;

    public OtpService(
        IApplicationDbContext context,
        ISmsService smsService,
        IDataEncryption dataEncryption,
        IOptions<OtpOptions> otpOptions)
    {
        _context = context;
        _smsService = smsService;
        _dataEncryption = dataEncryption;
        _otpOptions = otpOptions.Value;
    }

    public async Task<string> GenerateOtpAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        // Generate 6-digit OTP
        var random = new Random();
        var otp = random.Next(100000, 999999).ToString();

        // Encrypt OTP before storing
        var encryptedOtp = _dataEncryption.Encrypt(otp);

        // Store in database
        var phoneVerification = new PhoneVerification
        {
            PhoneNumber = phoneNumber,
            OtpCode = encryptedOtp,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_otpOptions.ExpiryMinutes),
            AttemptCount = 0,
            IsVerified = false,
            IsUsed = false,
            IsDeleted = false
        };

        _context.PhoneVerifications.Add(phoneVerification);
        await _context.SaveChanges(cancellationToken);

        return otp;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
    {
        var message = $"Your Cable verification code is: {otp}. This code expires in {_otpOptions.ExpiryMinutes} minutes.";
        return await _smsService.SendSmsAsync(phoneNumber, message, cancellationToken);
    }

    public async Task<bool> VerifyOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
    {
        var verification = await _context.PhoneVerifications
            .Where(x => x.PhoneNumber == phoneNumber && !x.IsDeleted && !x.IsUsed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (verification == null || verification.ExpiresAt < DateTime.UtcNow)
            return false;

        // Increment attempt count
        verification.AttemptCount++;

        if (verification.AttemptCount > _otpOptions.MaxAttempts)
        {
            verification.IsDeleted = true;
            await _context.SaveChanges(cancellationToken);
            return false;
        }

        // Decrypt and verify OTP
        var decryptedOtp = _dataEncryption.Decrypt(verification.OtpCode);
        if (decryptedOtp != otp)
        {
            await _context.SaveChanges(cancellationToken);
            return false;
        }

        // Mark as verified and used
        verification.IsVerified = true;
        verification.IsUsed = true;
        await _context.SaveChanges(cancellationToken);

        return true;
    }

    public async Task<bool> IsRateLimitedAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var recentRequests = await _context.PhoneVerifications
            .Where(x => x.PhoneNumber == phoneNumber && 
                       x.CreatedAt > DateTime.UtcNow.AddMinutes(-_otpOptions.RateLimitMinutes) &&
                       !x.IsDeleted)
            .CountAsync(cancellationToken);

        return recentRequests >= _otpOptions.MaxRequestsPerWindow;
    }
}
```

#### 3.2 SMS Service Integration
```csharp
// Infrastructure/Services/ISmsService.cs
public interface ISmsService
{
    Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}

// Infrastructure/Services/SmsService.cs
public class SmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<SmsOptions> _smsOptions;
    private readonly ILogger<SmsService> _logger;

    public SmsService(HttpClient httpClient, IOptions<SmsOptions> smsOptions, ILogger<SmsService> logger)
    {
        _httpClient = httpClient;
        _smsOptions = smsOptions.Value;
        _logger = logger;
    }

    public async Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        try
        {
            // Replace with your SMS service API implementation
            var request = new
            {
                to = phoneNumber,
                message = message,
                from = _smsOptions.SenderId
            };

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_smsOptions.ApiKey}");
            
            var response = await _httpClient.PostAsJsonAsync(_smsOptions.ApiUrl, request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("SMS sent successfully to {PhoneNumber}", phoneNumber);
                return true;
            }

            _logger.LogError("Failed to send SMS to {PhoneNumber}. Status: {StatusCode}", phoneNumber, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS to {PhoneNumber}", phoneNumber);
            return false;
        }
    }
}
```

### Step 4: Configuration Options

#### 4.1 Add Configuration Classes
```csharp
// Infrastructure/Options/SmsOptions.cs
public class SmsOptions
{
    public const string ConfigName = "SmsService";
    
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "Cable";
}

// Infrastructure/Options/OtpOptions.cs
public class OtpOptions
{
    public const string ConfigName = "OtpSettings";
    
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;
    public int RateLimitMinutes { get; set; } = 1;
    public int MaxRequestsPerWindow { get; set; } = 1;
}
```

#### 4.2 Update appsettings.json
```json
{
  "SmsService": {
    "ApiUrl": "https://your-sms-service-api.com/send",
    "ApiKey": "your-sms-api-key",
    "SenderId": "Cable"
  },
  "OtpSettings": {
    "ExpiryMinutes": 5,
    "MaxAttempts": 3,
    "RateLimitMinutes": 1,
    "MaxRequestsPerWindow": 1
  }
}
```

### Step 5: WebApi Layer - Request/Response Models

#### 5.1 Request Models
```csharp
// WebApi/Requests/Authentication/SendOtpRequest.cs
public class SendOtpRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

// WebApi/Requests/Authentication/VerifyOtpRequest.cs
public class VerifyOtpRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
}
```

### Step 6: WebApi Layer - API Endpoints

#### 6.1 Add Phone Authentication Routes
```csharp
// Update WebApi/Routes/UserRoutes.cs - Add to MapAuthenticationRoutes method

// Add these endpoints to the existing MapAuthenticationRoutes method:

app.MapPost("/send-otp", async (SendOtpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
    Results.Ok(await mediator.Send(new SendOtpCommand(request.PhoneNumber), cancellationToken)))
    .Produces<SendOtpResult>()
    .ProducesValidationProblem()
    .ProducesInternalServerError()
    .WithName("Send OTP")
    .WithSummary("Send OTP to phone number for authentication")
    .WithOpenApi(op =>
    {
        op.RequestBody.Required = true;
        return op;
    });

app.MapPost("/verify-otp", async (VerifyOtpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
    Results.Ok(await mediator.Send(new VerifyOtpCommand(request.PhoneNumber, request.OtpCode), cancellationToken)))
    .Produces<UserLoginResult>()
    .ProducesValidationProblem()
    .ProducesUnAuthorized()
    .ProducesInternalServerError()
    .WithName("Verify OTP")
    .WithSummary("Verify OTP and authenticate user")
    .WithOpenApi(op =>
    {
        op.RequestBody.Required = true;
        return op;
    });
```

### Step 7: Dependency Injection Updates

#### 7.1 Update Infrastructure DependencyInjection
```csharp
// Update Infrastructure/DependencyInjection.cs - Add to RegisterRepositories or create new method

private static IServiceCollection RegisterOtpServices(this IServiceCollection services, IConfiguration configuration)
{
    // Configure OTP options
    services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.ConfigName));
    services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.ConfigName));
    
    // Register services
    services.AddScoped<IOtpService, OtpService>();
    services.AddScoped<ISmsService, SmsService>();
    
    return services;
}

// Add this call to AddInfrastructure method:
.RegisterOtpServices(configurations)
```

#### 7.2 Update Authentication Service
```csharp
// Update Infrastructure/Identity/AuthenticationService.cs - Add these methods:

public async Task<string> SendOtpAsync(string phoneNumber, CancellationToken cancellationToken)
{
    ExceptionHelper.ThrowIfNullOrEmpty(phoneNumber);
    
    var otpService = // inject IOtpService
    
    if (await otpService.IsRateLimitedAsync(phoneNumber, cancellationToken))
    {
        throw new InvalidOperationException("Rate limit exceeded. Please try again later.");
    }

    var otp = await otpService.GenerateOtpAsync(phoneNumber, cancellationToken);
    var sent = await otpService.SendOtpAsync(phoneNumber, otp, cancellationToken);
    
    if (!sent)
    {
        throw new InvalidOperationException("Failed to send OTP. Please try again.");
    }
    
    return "OTP sent successfully";
}

public async Task<UserLoginResult> LoginWithOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
{
    ExceptionHelper.ThrowIfNullOrEmpty(phoneNumber);
    ExceptionHelper.ThrowIfNullOrEmpty(otp);
    
    var otpService = // inject IOtpService
    
    var isValid = await otpService.VerifyOtpAsync(phoneNumber, otp, cancellationToken);
    if (!isValid)
    {
        throw new NotAuthorizedAccessException("Invalid or expired OTP");
    }

    // Find or create user by phone number
    var user = await _applicationDbContext.UserAccounts
        .FirstOrDefaultAsync(x => x.Phone == phoneNumber && !x.IsDeleted, cancellationToken);

    if (user == null)
    {
        // Create new user
        user = new UserAccount
        {
            Phone = phoneNumber,
            RoleId = 3, // Default role
            IsActive = true,
            IsDeleted = false,
            IsPhoneVerified = true,
            PhoneVerifiedAt = DateTime.UtcNow,
            Name = $"User_{phoneNumber.Substring(phoneNumber.Length - 4)}" // Default name
        };
        
        _applicationDbContext.UserAccounts.Add(user);
        await _applicationDbContext.SaveChanges(cancellationToken);
    }
    else
    {
        // Update verification status
        user.IsPhoneVerified = true;
        user.PhoneVerifiedAt = DateTime.UtcNow;
        await _applicationDbContext.SaveChanges(cancellationToken);
    }

    CheckAreUserDetailsValid(user);
    var isCompletedData = CheckUserDetailsCompleted(user);
    
    return await GetUserLoginDetails(user, isCompletedData, cancellationToken);
}
```

---

## Final API Endpoints

After complete implementation, you'll have these new endpoints:

- `POST /api/users/send-otp` - Send OTP to phone number
- `POST /api/users/verify-otp` - Verify OTP and authenticate user

## Security Considerations Implemented

1. **Rate Limiting**: Prevents spam and abuse
2. **OTP Encryption**: OTPs are encrypted before storage
3. **Expiry Time**: OTPs expire after 5 minutes
4. **Attempt Limiting**: Maximum 3 attempts per OTP
5. **Phone Validation**: Kuwait phone number format validation
6. **Cleanup Jobs**: Automatic cleanup of expired records

## Testing the Implementation

1. Run the SQL script to create database tables
2. Reverse engineer to update entity models
3. Implement the application layer step by step
4. Test with Postman or similar tool
5. Verify SMS delivery and OTP verification flow

This implementation follows your existing Clean Architecture patterns and maintains security best practices.