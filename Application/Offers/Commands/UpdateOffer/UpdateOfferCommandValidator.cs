using FluentValidation;

namespace Application.Offers.Commands.UpdateOffer;

/// <summary>
/// The update path had no validator at all, so every rule enforced when an offer
/// was proposed could be undone by editing it. Production carries an offer with
/// MaxUsesPerUser = -9 that reached that state this way.
///
/// Kept deliberately in step with <c>ProposeOfferCommandValidator</c>.
/// </summary>
public class UpdateOfferCommandValidator : AbstractValidator<UpdateOfferCommand>
{
    public UpdateOfferCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(255);

        RuleFor(x => x.ProviderType)
            .NotEmpty().WithMessage("Provider type is required")
            .Must(x => x is "ChargingPoint" or "ServiceProvider")
            .WithMessage("Provider type must be ChargingPoint or ServiceProvider");

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

        RuleFor(x => x.ValidTo)
            .Must((cmd, validTo) => validTo == null || validTo > cmd.ValidFrom)
            .WithMessage("Valid to must be after valid from");

        RuleFor(x => x.OfferCodeExpirySeconds)
            .GreaterThan(0).WithMessage("Offer code expiry must be greater than 0 seconds");

        // Redemption tests `usesSoFar >= MaxUsesPerUser`, so any value <= 0 is
        // true on the first attempt. The QR still generates and the customer is
        // refused only after scanning it.
        RuleFor(x => x.MaxUsesPerUser)
            .GreaterThan(0).When(x => x.MaxUsesPerUser.HasValue)
            .WithMessage("Max uses per user must be greater than 0, or left empty for unlimited");

        RuleFor(x => x.MaxTotalUses)
            .GreaterThan(0).When(x => x.MaxTotalUses.HasValue)
            .WithMessage("Max total uses must be greater than 0, or left empty for unlimited");
    }
}
