using FluentValidation;

namespace Cable.Requests.Providers;

public class ProviderSendOtpRequestValidator : AbstractValidator<ProviderSendOtpRequest>
{
    public ProviderSendOtpRequestValidator()
    {
        RuleFor(x => x.SessionToken)
            .NotEmpty()
            .WithMessage("Session token is required");
    }
}
