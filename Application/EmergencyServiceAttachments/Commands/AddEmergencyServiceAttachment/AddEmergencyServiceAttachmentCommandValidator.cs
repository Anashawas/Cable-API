using Application.Common.Interfaces;
using Application.Common.Localization;
using FluentValidation;

namespace Application.EmergencyServiceAttachments.Commands.AddEmergencyServiceAttachment;

public class AddEmergencyServiceAttachmentCommandValidator : AbstractValidator<AddEmergencyServiceAttachmentCommand>
{
    public AddEmergencyServiceAttachmentCommandValidator(IUploadFileService uploadFileService)
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Emergency service ID is required")
            .GreaterThan(0)
            .WithMessage("Emergency service ID must be greater than 0");

        RuleFor(x => x.Files)
            .NotEmpty()
            .WithMessage("At least one file is required")
            .Must(uploadFileService.IsValidExtension)
            .WithMessage(Resources.FileExtensionNotValid)
            .Must(uploadFileService.IsValidSize)
            .WithMessage(Resources.FileSizeNotValid);
    }
}
