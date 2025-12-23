using FluentValidation;

namespace Application.SharedLinks.Commands.CreateSharedLink;

public class CreateSharedLinkCommandValidator : AbstractValidator<CreateSharedLinkCommand>
{
    public CreateSharedLinkCommandValidator()
    {
        RuleFor(v => v.LinkType)
            .NotEmpty().WithMessage("LinkType is required.")
            .MaximumLength(50).WithMessage("LinkType must not exceed 50 characters.");

        RuleFor(v => v.MaxUsage)
            .GreaterThan(0).WithMessage("MaxUsage must be greater than 0.")
            .LessThanOrEqualTo(1000).WithMessage("MaxUsage must not exceed 1000.");

        RuleFor(v => v.ExpiresAt)
            .Must(BeValidExpiryDate).WithMessage("ExpiresAt must be in the future.")
            .When(v => v.ExpiresAt.HasValue);

        RuleFor(v => v.Parameters)
            .MaximumLength(4000).WithMessage("Parameters must not exceed 4000 characters.")
            .When(v => !string.IsNullOrEmpty(v.Parameters));
    }

    private static bool BeValidExpiryDate(DateTime? expiresAt)
    {
        return !expiresAt.HasValue || expiresAt.Value > DateTime.Now;
    }
}