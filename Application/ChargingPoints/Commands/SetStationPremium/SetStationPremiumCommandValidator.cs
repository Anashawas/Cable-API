using FluentValidation;

namespace Application.ChargingPoints.Commands.SetStationPremium;

public class SetStationPremiumCommandValidator : AbstractValidator<SetStationPremiumCommand>
{
    public SetStationPremiumCommandValidator()
    {
        RuleFor(x => x.ChargingPointId)
            .GreaterThan(0);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => x.PaymentDate)
            .WithMessage("ExpiresAt must be after PaymentDate.");

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Amount.HasValue);

        RuleFor(x => x.Note)
            .MaximumLength(500);
    }
}
