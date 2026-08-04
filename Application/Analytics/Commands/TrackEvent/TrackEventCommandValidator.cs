using FluentValidation;

namespace Application.Analytics.Commands.TrackEvent;

public class TrackEventCommandValidator : AbstractValidator<TrackEventCommand>
{
    public TrackEventCommandValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty()
            .Must(AnalyticsCatalog.IsKnownEntityType)
            .WithMessage("EntityType must be one of: ChargingPoint, ServiceProvider, Banner.");

        RuleFor(x => x.EntityId)
            .GreaterThan(0);

        RuleFor(x => x.EventType)
            .IsInEnum()
            .WithMessage("Unknown analytics event type.");

        // The event type must be legal for the entity type
        // (e.g. BannerClick only on Banner, CallButtonClick only on a provider).
        RuleFor(x => x)
            .Must(x => AnalyticsCatalog.IsValidCombination(x.EntityType, x.EventType))
            .WithMessage("This event type is not valid for the given entity type.")
            .When(x => !string.IsNullOrEmpty(x.EntityType) && AnalyticsCatalog.IsKnownEntityType(x.EntityType));

        RuleFor(x => x.AnonymousId).MaximumLength(100);
        RuleFor(x => x.Source).MaximumLength(20);
    }
}
