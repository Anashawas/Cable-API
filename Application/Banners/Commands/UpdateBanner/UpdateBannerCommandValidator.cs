using Cable.Core.Utilities;
using FluentValidation;

namespace Application.Banners.Commands.UpdateBanner;

public class UpdateBannerCommandValidator : AbstractValidator<UpdateBannerCommand>
{

    public UpdateBannerCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Phone)
            .NotEmpty()
            .MaximumLength(50)
            .Must(phone => string.IsNullOrEmpty(phone) || PhoneNumberUtility.IsValidJordanPhoneNumber(phone))
            .WithMessage($"Phone number must be a valid Jordan mobile number. Supported formats: {string.Join(", ", PhoneNumberUtility.GetSupportedFormats())}");
    }
}