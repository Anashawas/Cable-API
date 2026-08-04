using Application.Common.Interfaces;
using Cable.Security.Encryption.Interfaces;
using Domain.Enitites;
using Infrastructrue.Common.Localization;
using Infrastructrue.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Services;

public class OtpService : IOtpService
{
    private readonly IApplicationDbContext _context;
    private readonly ISmsService _smsService;
    private readonly IDataEncryption _dataEncryption;
    private readonly OtpOptions _otpOptions;

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

    /// <summary>
    /// True when this number should use the fixed OTP and skip the SMS gateway, i.e. either:
    ///   - dev/QA mode is on (TestModeForAllNumbers — applies to every number), or
    ///   - the number is an allow-listed demo/review account (e.g. App Store review).
    /// </summary>
    private bool IsTestPhoneNumber(string? phoneNumber)
    {
        // Development mode: no number ever receives a paid SMS.
        if (_otpOptions.TestModeForAllNumbers)
            return true;

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return false;

        var numbers = _otpOptions.TestPhoneNumbers;
        if (numbers is null || numbers.Length == 0)
            return false;

        return numbers.Any(n =>
            !string.IsNullOrWhiteSpace(n) &&
            string.Equals(n.Trim(), phoneNumber.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The request validators enforce ^\d{6}$, so a misconfigured TestOtp would be rejected
    /// with a 400 before it ever reaches verification. Fall back to a valid 6-digit code
    /// rather than silently handing out one that can never be submitted.
    /// </summary>
    private string GetFixedTestOtp()
    {
        var configured = _otpOptions.TestOtp?.Trim();

        return !string.IsNullOrEmpty(configured)
               && configured.Length == 6
               && configured.All(char.IsDigit)
            ? configured
            : "000000";
    }

    public async Task<string> GenerateOtpAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        // Demo/review accounts always get the same fixed code.
        var otp = IsTestPhoneNumber(phoneNumber)
            ? GetFixedTestOtp()
            : new Random().Next(100000, 999999).ToString();

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
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.PhoneVerifications.Add(phoneVerification);
        await _context.SaveChanges(cancellationToken);

        return otp;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
    {
        // Never send a real SMS to a demo/review account — the code is already known.
        if (IsTestPhoneNumber(phoneNumber))
            return true;

        var message = string.Format(Resources.OtpMessage, otp, _otpOptions.ExpiryMinutes);
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
        verification.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChanges(cancellationToken);

        return true;
    }

    public async Task<bool> IsRateLimitedAsync(string phoneNumber, CancellationToken cancellationToken)
    =>
        // Reviewers may retry many times — don't rate-limit demo/review accounts.
        !IsTestPhoneNumber(phoneNumber) &&
         await _context.PhoneVerifications
            .Where(x => x.PhoneNumber == phoneNumber &&
                        x.CreatedAt >= DateTime.UtcNow.AddMinutes(-_otpOptions.RateLimitMinutes) &&
                        x.IsDeleted == false)
            .CountAsync(cancellationToken) >= _otpOptions.MaxRequestsPerWindow;
    
}