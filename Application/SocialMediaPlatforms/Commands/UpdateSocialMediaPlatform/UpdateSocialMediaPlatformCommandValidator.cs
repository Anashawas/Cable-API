using FluentValidation;

namespace Application.SocialMediaPlatforms.Commands.UpdateSocialMediaPlatform;

public class UpdateSocialMediaPlatformCommandValidator : AbstractValidator<UpdateSocialMediaPlatformCommand>
{
    public UpdateSocialMediaPlatformCommandValidator()
    {
        RuleFor(v => v.Id).GreaterThan(0);

        RuleFor(v => v.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");

        RuleFor(v => v.NameAr)
            .MaximumLength(100).WithMessage("NameAr must not exceed 100 characters.")
            .When(v => !string.IsNullOrEmpty(v.NameAr));

        RuleFor(v => v.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("DisplayOrder must be >= 0.");
    }
}
