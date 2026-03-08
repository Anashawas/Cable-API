using FluentValidation;

namespace Application.ConversionRates.Commands.CreateConversionRate;

public class CreateConversionRateCommandValidator : AbstractValidator<CreateConversionRateCommand>
{
    public CreateConversionRateCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(255);

        RuleFor(x => x.CurrencyCode)
            .NotEmpty().WithMessage("Currency code is required")
            .MaximumLength(10);

        RuleFor(x => x.PointsPerUnit)
            .GreaterThan(0).WithMessage("Points per unit must be greater than 0");
    }
}
