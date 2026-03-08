using FluentValidation;

namespace Application.Users.Commands.VerifyPhone.VerifyUserPhone;

public class VerifyUserPhoneCommandValidator : AbstractValidator<VerifyUserPhoneCommand>
{
    public VerifyUserPhoneCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Phone number is required");

        RuleFor(x => x.OtpCode)
            .NotEmpty()
            .WithMessage("OTP code is required")
            .Length(6)
            .WithMessage("OTP code must be 6 digits")
            .Matches(@"^\d{6}$")
            .WithMessage("OTP code must contain only digits");
    }
}
