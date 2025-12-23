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
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.PhoneVerifications.Add(phoneVerification);
        await _context.SaveChanges(cancellationToken);

        return otp;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
    {
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
         await _context.PhoneVerifications
            .Where(x => x.PhoneNumber == phoneNumber &&
                        x.CreatedAt >= DateTime.UtcNow.AddMinutes(-_otpOptions.RateLimitMinutes) &&
                        x.IsDeleted == false)
            .CountAsync(cancellationToken) >= _otpOptions.MaxRequestsPerWindow;
    
}