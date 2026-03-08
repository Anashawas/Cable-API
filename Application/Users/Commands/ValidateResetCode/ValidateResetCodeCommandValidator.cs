using FluentValidation;

namespace Application.Users.Commands.ValidateResetCode;

public class ValidateResetCodeCommandValidator : AbstractValidator<ValidateResetCodeCommand>
{
    public ValidateResetCodeCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email is required")
            .EmailAddress()
            .WithMessage("Invalid email format");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Reset code is required")
            .Length(6)
            .WithMessage("Reset code must be 6 digits")
            .Matches(@"^\d{6}$")
            .WithMessage("Reset code must contain only digits");
    }
}
