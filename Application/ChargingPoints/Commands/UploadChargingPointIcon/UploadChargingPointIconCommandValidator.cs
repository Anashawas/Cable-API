using Application.Common.Localization;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Application.ChargingPoints.Commands.UploadChargingPointIcon;

public class UploadChargingPointIconCommandValidator:AbstractValidator<UploadChargingPointIconCommand>
{
    private readonly IUploadFileService _uploadFileService;

    public UploadChargingPointIconCommandValidator(IUploadFileService uploadFileService)
    {
        _uploadFileService = uploadFileService;
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.File).NotEmpty().Must(IsValidSize).WithMessage(Resources.FileSizeNotValid).Must(IsValidExtension).WithMessage(Resources.FileExtensionNotValid);
    }
    private bool IsValidSize(IFormFile file)
        =>  _uploadFileService.IsValidSize(new FormFileCollection(){file});
    
    private bool IsValidExtension(IFormFile file)
        =>  _uploadFileService.IsValidExtension(new FormFileCollection(){file});
}