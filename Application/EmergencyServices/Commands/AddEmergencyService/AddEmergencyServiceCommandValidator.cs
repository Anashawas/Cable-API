using FluentValidation;

namespace Application.EmergencyServices.Commands.AddEmergencyService;

public class AddEmergencyServiceCommandValidator : AbstractValidator<AddEmergencyServiceCommand>
{
    public AddEmergencyServiceCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required")
            .MaximumLength(255)
            .WithMessage("Title cannot exceed 255 characters");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Description cannot exceed 1000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.ImageUrl)
            .MaximumLength(500)
            .WithMessage("Image URL cannot exceed 500 characters")
            .When(x => x.ImageUrl != null);

        RuleFor(x => x.SubscriptionType)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SubscriptionType must be 0 (Normal) or 1 (Premium)");

        RuleFor(x => x.PriceDetails)
            .MaximumLength(255)
            .WithMessage("Price details cannot exceed 255 characters")
            .When(x => x.PriceDetails != null);

        RuleFor(x => x.ActionUrl)
            .MaximumLength(500)
            .WithMessage("Action URL cannot exceed 500 characters")
            .When(x => x.ActionUrl != null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(40)
            .WithMessage("Phone number cannot exceed 40 characters")
            .When(x => x.PhoneNumber != null);

        RuleFor(x => x.WhatsAppNumber)
            .MaximumLength(40)
            .WithMessage("WhatsApp number cannot exceed 40 characters")
            .When(x => x.WhatsAppNumber != null);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Sort order must be greater than or equal to 0");
    }
}
