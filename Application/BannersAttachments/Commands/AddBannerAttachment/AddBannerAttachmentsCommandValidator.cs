using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.BannersAttachments.Commands;

public class AddBannerAttachmentsCommandValidator : AbstractValidator<AddBannerAttachmentsCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public AddBannerAttachmentsCommandValidator(IApplicationDbContext applicationDbContext,
        IUploadFileService uploadFileService)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.BannerId).NotEmpty().MustAsync(CheckBannerIsExist).WithMessage(Resources.CheckBannerIsExist);

        RuleFor(x => x.Files).NotEmpty().Must(uploadFileService.IsValidExtension)
            .WithMessage(Resources.FileExtensionNotValid)
            .Must(uploadFileService.IsValidSize).WithMessage(Resources.FileSizeNotValid);
    }

    private async Task<bool> CheckBannerIsExist(int bannerId, CancellationToken cancellationToken)
    {
        return !await _applicationDbContext.Banners.AnyAsync(
            x => x.Id == bannerId, cancellationToken);
    }
}