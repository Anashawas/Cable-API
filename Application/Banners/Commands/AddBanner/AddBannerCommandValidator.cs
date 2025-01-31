using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Commands.AddBanner;

public class AddBannerCommandValidator : AbstractValidator<AddBannerCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public AddBannerCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(50);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty();
        RuleFor(x => x)
            .Must(duration => duration.StartDate <= duration.EndDate)
            .WithMessage(Resources.StartDateMustBeLessThanEndDate);
        
        RuleFor(x => x).MustAsync(CheckBannerIsExist).WithMessage(Resources.UserBannerAlreadyExist);
    }


    private async Task<bool> CheckBannerIsExist(AddBannerCommand command, CancellationToken cancellationToken)
    {
        return !await _applicationDbContext.Banners.AnyAsync(
            x => x.Name == command.Name || x.Email == command.Email || x.Phone == command.Phone, cancellationToken);
    }
}