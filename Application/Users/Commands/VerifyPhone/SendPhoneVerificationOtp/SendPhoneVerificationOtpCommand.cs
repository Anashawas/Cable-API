using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.VerifyPhone.SendPhoneVerificationOtp;

public record SendPhoneVerificationOtpCommand(string PhoneNumber) : IRequest<SendPhoneVerificationOtpDto>;

public class SendPhoneVerificationOtpCommandHandler : IRequestHandler<SendPhoneVerificationOtpCommand, SendPhoneVerificationOtpDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IOtpService _otpService;
    private readonly ICurrentUserService _currentUserService;

    public SendPhoneVerificationOtpCommandHandler(
        IApplicationDbContext context,
        IOtpService otpService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _otpService = otpService;
        _currentUserService = currentUserService;
    }

    public async Task<SendPhoneVerificationOtpDto> Handle(SendPhoneVerificationOtpCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.UserId.HasValue)
        {
            throw new NotAuthorizedAccessException();
        }

        var user = await _context.UserAccounts
            .FirstOrDefaultAsync(x => x.Id == _currentUserService.UserId.Value && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        var normalizedPhoneNumber = PhoneNumberUtility.NormalizePhoneNumber(request.PhoneNumber);
        if (normalizedPhoneNumber == null)
        {
            throw new DataValidationException("PhoneNumber", "Invalid phone number format. Please use a valid Jordan mobile number.");
        }

        var existingUserWithPhone = await _context.UserAccounts
            .FirstOrDefaultAsync(x => x.Phone == normalizedPhoneNumber && x.Id != user.Id && !x.IsDeleted, cancellationToken);

        if (existingUserWithPhone != null)
        {
            throw new DataValidationException("PhoneNumber", "This phone number is already linked to another account.");
        }

        if (await _otpService.IsRateLimitedAsync(normalizedPhoneNumber, cancellationToken))
        {
            throw new DataValidationException("MaxRequestsPerWindow", "Rate limit exceeded. Please try again later.");
        }

        var otp = await _otpService.GenerateOtpAsync(normalizedPhoneNumber, cancellationToken);
        var sent = await _otpService.SendOtpAsync(normalizedPhoneNumber, otp, cancellationToken);

        if (!sent)
        {
            throw new CableApplicationException("Failed to send OTP. Please try again.");
        }

        return new SendPhoneVerificationOtpDto(
            true,
            "OTP sent successfully to your phone number",
            DateTime.UtcNow.AddMinutes(5));
    }
}
