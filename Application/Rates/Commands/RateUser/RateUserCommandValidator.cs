using FluentValidation;

namespace Application.Rates.Commands.RateUser;

public class RateUserCommandValidator : AbstractValidator<RateUserCommand>
{
    public RateUserCommandValidator()
    {
        RuleFor(x => x.PartnerTransactionId).GreaterThan(0);

        // Bounded here rather than in the handler so an out-of-range score is a
        // 400 with a field name, and so the stored average can never be skewed
        // by a 0 or a 99.
        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("Rating must be between 1 and 5");

        RuleFor(x => x.Comment)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.Comment));
    }
}
