using Application.Common.Localization;
using FluentValidation;

namespace Application.OfferAttachments.Commands.AddOfferAttachment;

public class AddOfferAttachmentCommandValidator : AbstractValidator<AddOfferAttachmentCommand>
{
    public AddOfferAttachmentCommandValidator(IUploadFileService uploadFileService)
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Files).NotEmpty()
            .Must(uploadFileService.IsValidExtension).WithMessage(Resources.FileExtensionNotValid)
            .Must(uploadFileService.IsValidSize).WithMessage(Resources.FileSizeNotValid);
    }
}
