using FluentValidation;

namespace Application.ChargerBrands.Commands.AddChargerBrand;

public class AddChargerBrandCommandValidator : AbstractValidator<AddChargerBrandCommand>
{
    public AddChargerBrandCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);
    }
}
