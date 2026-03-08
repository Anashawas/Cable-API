using FluentValidation;

namespace Cable.Requests.Providers;

public class ProviderVerifyOtpRequestValidator : AbstractValidator<ProviderVerifyOtpRequest>
{
    public ProviderVerifyOtpRequestValidator()
    {
        RuleFor(x => x.SessionToken)
            .NotEmpty()
            .WithMessage("Session token is required");

        RuleFor(x => x.OtpCode)
            .NotEmpty()
            .WithMessage("OTP code is required")
            .Length(6)
            .WithMessage("OTP code must be 6 digits")
            .Matches(@"^\d{6}$")
            .WithMessage("OTP code must contain only digits");
    }
}
