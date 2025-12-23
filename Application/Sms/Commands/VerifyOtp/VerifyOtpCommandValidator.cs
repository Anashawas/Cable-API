using FluentValidation;
using Cable.Core.Utilities;

namespace Application.Authentication.Commands.VerifyOtp;

public class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(PhoneNumberUtility.IsValidJordanPhoneNumber)
            .WithMessage($"Phone number must be a valid Jordan mobile number. Supported formats: {string.Join(", ", PhoneNumberUtility.GetSupportedFormats())}");

        RuleFor(x => x.OtpCode)
            .NotEmpty()
            .Length(6)
            .Matches(@"^\d{6}$")
            .WithMessage("OTP must be 6 digits");
    }
}