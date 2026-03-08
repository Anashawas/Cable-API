using FluentValidation;

namespace Application.ServiceProviders.Commands.CreateServiceProvider;

public class CreateServiceProviderCommandValidator : AbstractValidator<CreateServiceProviderCommand>
{
    public CreateServiceProviderCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(255).WithMessage("Name must not exceed 255 characters");

        RuleFor(x => x.ServiceCategoryId)
            .GreaterThan(0).WithMessage("ServiceCategoryId must be greater than 0");

        RuleFor(x => x.StatusId)
            .GreaterThan(0).WithMessage("StatusId must be greater than 0");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180");
    }
}
