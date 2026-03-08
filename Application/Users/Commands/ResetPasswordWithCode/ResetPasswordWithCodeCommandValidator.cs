using FluentValidation;

namespace Application.Users.Commands.ResetPasswordWithCode;

public class ResetPasswordWithCodeCommandValidator : AbstractValidator<ResetPasswordWithCodeCommand>
{
    public ResetPasswordWithCodeCommandValidator()
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

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("New password is required")
            .MinimumLength(3)
            .WithMessage("Password must be at least 3 characters")
            .MaximumLength(20)
            .WithMessage("Password must not exceed 20 characters");
    }
}
