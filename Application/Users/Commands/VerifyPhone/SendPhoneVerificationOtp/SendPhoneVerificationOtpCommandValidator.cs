using FluentValidation;

namespace Application.Users.Commands.VerifyPhone.SendPhoneVerificationOtp;

public class SendPhoneVerificationOtpCommandValidator : AbstractValidator<SendPhoneVerificationOtpCommand>
{
    public SendPhoneVerificationOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Phone number is required");
    }
}
