using FluentValidation;

namespace Cable.Requests.Providers;

public class ProviderLoginRequestValidator : AbstractValidator<ProviderLoginRequest>
{
    public ProviderLoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email is required")
            .EmailAddress()
            .WithMessage("Valid email address is required");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required");

    }
}
