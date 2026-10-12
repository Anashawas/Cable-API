using Cable.Core.Utilities;
using FluentValidation;

namespace Application.Banners.Commands.UpdateBanner;

public class UpdateBannerCommandValidator : AbstractValidator<UpdateBannerCommand>
{

    public UpdateBannerCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(50);
        // Any dialable number (mobile, landline, short code, international) — not only Jordan mobiles.
        BannerRules.AddActionRules(this, x => x.ActionType, x => x.ActionUrl, x => x.Phone);
    }
}