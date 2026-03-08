using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.VerifyPhone.VerifyUserPhone;

public record VerifyUserPhoneCommand(
    string PhoneNumber,
    string OtpCode
) : IRequest<VerifyUserPhoneDto>;

public class VerifyUserPhoneCommandHandler : IRequestHandler<VerifyUserPhoneCommand, VerifyUserPhoneDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IOtpService _otpService;
    private readonly ICurrentUserService _currentUserService;

    public VerifyUserPhoneCommandHandler(
        IApplicationDbContext context,
        IOtpService otpService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _otpService = otpService;
        _currentUserService = currentUserService;
    }

    public async Task<VerifyUserPhoneDto> Handle(VerifyUserPhoneCommand request, CancellationToken cancellationToken)
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

        var isValid = await _otpService.VerifyOtpAsync(normalizedPhoneNumber, request.OtpCode, cancellationToken);
        if (!isValid)
        {
            throw new DataValidationException("OtpCode", "Invalid or expired OTP code");
        }

        user.Phone = normalizedPhoneNumber;
        user.IsPhoneVerified = true;
        user.PhoneVerifiedAt = DateTime.UtcNow;

        await _context.SaveChanges(cancellationToken);

        return new VerifyUserPhoneDto(
            true,
            "Phone number verified and linked to your account successfully",
            normalizedPhoneNumber,
            DateTime.UtcNow);
    }
}
