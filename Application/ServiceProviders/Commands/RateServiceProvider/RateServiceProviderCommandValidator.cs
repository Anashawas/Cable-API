using FluentValidation;

namespace Application.ServiceProviders.Commands.RateServiceProvider;

public class RateServiceProviderCommandValidator : AbstractValidator<RateServiceProviderCommand>
{
    public RateServiceProviderCommandValidator()
    {
        RuleFor(x => x.ServiceProviderId)
            .GreaterThan(0).WithMessage("ServiceProviderId must be greater than 0");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5");
    }
}
