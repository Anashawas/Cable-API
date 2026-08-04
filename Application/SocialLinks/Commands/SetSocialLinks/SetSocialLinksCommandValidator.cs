using FluentValidation;

namespace Application.SocialLinks.Commands.SetSocialLinks;

public class SetSocialLinksCommandValidator : AbstractValidator<SetSocialLinksCommand>
{
    private static readonly string[] AllowedProviderTypes = { "ServiceProvider", "ChargingPoint" };

    public SetSocialLinksCommandValidator()
    {
        RuleFor(v => v.ProviderType)
            .NotEmpty().WithMessage("ProviderType is required.")
            .Must(t => AllowedProviderTypes.Contains(t))
            .WithMessage("ProviderType must be 'ServiceProvider' or 'ChargingPoint'.");

        RuleFor(v => v.ProviderId)
            .GreaterThan(0).WithMessage("ProviderId must be greater than 0.");

        RuleFor(v => v.Links).NotNull();

        RuleForEach(v => v.Links).ChildRules(link =>
        {
            link.RuleFor(x => x.SocialMediaPlatformId)
                .GreaterThan(0).WithMessage("SocialMediaPlatformId must be greater than 0.");

            link.RuleFor(x => x.Url)
                .NotEmpty().WithMessage("Url is required.")
                .MaximumLength(1000).WithMessage("Url must not exceed 1000 characters.");

            link.RuleFor(x => x.DisplayOrder)
                .GreaterThanOrEqualTo(0);
        });
    }
}
