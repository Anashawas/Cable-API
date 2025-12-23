using Application.BannersAttachments.Commands.AddBannerAttachment;
using Application.Common.Localization;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.BannersAttachments.Commands;

public class AddBannerAttachmentsCommandValidator : AbstractValidator<AddBannerAttachmentsCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    private readonly IUploadFileService _uploadFileService;
    public AddBannerAttachmentsCommandValidator(IApplicationDbContext applicationDbContext,
        IUploadFileService uploadFileService)
    {
        _applicationDbContext = applicationDbContext;
        _uploadFileService = uploadFileService;
        RuleFor(x => x.BannerId).NotEmpty();

        RuleFor(x => x.Files).NotEmpty().Must(uploadFileService.IsValidExtension)
            .WithMessage(Resources.FileExtensionNotValid)
            .Must(uploadFileService.IsValidSize).WithMessage(Resources.FileSizeNotValid);
    }

 

}