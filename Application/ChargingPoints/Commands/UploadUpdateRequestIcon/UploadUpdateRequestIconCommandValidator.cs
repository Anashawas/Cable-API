using Application.Common.Interfaces;
using Application.Common.Localization;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Application.ChargingPoints.Commands.UploadUpdateRequestIcon;

public class UploadUpdateRequestIconCommandValidator : AbstractValidator<UploadUpdateRequestIconCommand>
{
    private readonly IUploadFileService _uploadFileService;

    public UploadUpdateRequestIconCommandValidator(IUploadFileService uploadFileService)
    {
        _uploadFileService = uploadFileService;

        RuleFor(x => x.UpdateRequestId).GreaterThan(0);
        RuleFor(x => x.File)
            .NotEmpty()
            .Must(IsValidSize)
            .WithMessage(Resources.FileSizeNotValid)
            .Must(IsValidExtension)
            .WithMessage(Resources.FileExtensionNotValid);
    }

    private bool IsValidSize(IFormFile file)
        => _uploadFileService.IsValidSize(new FormFileCollection() { file });

    private bool IsValidExtension(IFormFile file)
        => _uploadFileService.IsValidExtension(new FormFileCollection() { file });
}
