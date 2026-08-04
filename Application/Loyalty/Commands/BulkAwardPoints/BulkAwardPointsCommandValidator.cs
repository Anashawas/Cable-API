using FluentValidation;

namespace Application.Loyalty.Commands.BulkAwardPoints;

public class BulkAwardPointsCommandValidator : AbstractValidator<BulkAwardPointsCommand>
{
    public BulkAwardPointsCommandValidator()
    {
        RuleFor(x => x.Points)
            .GreaterThan(0)
            .LessThanOrEqualTo(100_000);

        RuleFor(x => x.Note)
            .MaximumLength(400);

        // Guard against accidentally awarding the whole user base:
        // at least one segment filter must be provided.
        RuleFor(x => x)
            .Must(x => x.CarTypeId.HasValue || x.CarModelId.HasValue
                       || !string.IsNullOrWhiteSpace(x.City) || x.TierId.HasValue)
            .WithMessage("At least one segment filter (carTypeId, carModelId, city, tierId) is required.");
    }
}
