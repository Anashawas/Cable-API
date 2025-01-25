using Application.Common.Localization;
using FluentValidation;

namespace Application.ChargingPointAttachments.Command;

public class AddChargingPointAttachmentCommandValidator : AbstractValidator<AddChargingPointAttachmentCommand>
{

    public AddChargingPointAttachmentCommandValidator(IUploadFileService uploadFileService)
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Files).NotEmpty()
            .Must(uploadFileService.IsValidExtension).WithMessage(Resources.FileExtensionNotValid)
            .Must(uploadFileService.IsValidSize).WithMessage(Resources.FileSizeNotValid);
    }
}