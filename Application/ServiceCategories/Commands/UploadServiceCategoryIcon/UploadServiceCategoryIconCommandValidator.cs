using Application.Common.Localization;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Application.ServiceCategories.Commands.UploadServiceCategoryIcon;

public class UploadServiceCategoryIconCommandValidator : AbstractValidator<UploadServiceCategoryIconCommand>
{
    private readonly IUploadFileService _uploadFileService;

    public UploadServiceCategoryIconCommandValidator(IUploadFileService uploadFileService)
    {
        _uploadFileService = uploadFileService;
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.File).NotEmpty()
            .Must(IsValidSize).WithMessage(Resources.FileSizeNotValid)
            .Must(IsValidExtension).WithMessage(Resources.FileExtensionNotValid);
    }

    private bool IsValidSize(IFormFile file)
        => _uploadFileService.IsValidSize(new FormFileCollection { file });

    private bool IsValidExtension(IFormFile file)
        => _uploadFileService.IsValidExtension(new FormFileCollection { file });
}
