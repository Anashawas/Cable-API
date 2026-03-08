using FluentValidation;

namespace Application.Offers.Commands.ProposeOffer;

public class ProposeOfferCommandValidator : AbstractValidator<ProposeOfferCommand>
{
    public ProposeOfferCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(255);

        RuleFor(x => x.ProviderType)
            .NotEmpty().WithMessage("Provider type is required")
            .Must(x => x is "ChargingPoint" or "ServiceProvider")
            .WithMessage("Provider type must be 'ChargingPoint' or 'ServiceProvider'");

        RuleFor(x => x.ProviderId)
            .GreaterThan(0).WithMessage("Provider ID must be greater than 0");

        RuleFor(x => x.PointsCost)
            .GreaterThan(0).WithMessage("Points cost must be greater than 0");

        RuleFor(x => x.MonetaryValue)
            .GreaterThan(0).WithMessage("Monetary value must be greater than 0");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty().WithMessage("Currency code is required")
            .MaximumLength(10);

        RuleFor(x => x.ValidFrom)
            .NotEmpty().WithMessage("Valid from date is required");

        RuleFor(x => x.OfferCodeExpiryMinutes)
            .GreaterThan(0).WithMessage("Offer code expiry must be greater than 0 minutes");
    }
}
