using FluentValidation;
using Cable.Core.Utilities;

namespace Application.Authentication.Commands.SendOtp;

public class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(PhoneNumberUtility.IsValidJordanPhoneNumber)
            .WithMessage($"Phone number must be a valid Jordan mobile number. Supported formats: {string.Join(", ", PhoneNumberUtility.GetSupportedFormats())}");
    }
}