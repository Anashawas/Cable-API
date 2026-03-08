using Application.Common.Interfaces;
using Application.Common.Localization;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Application.ChargingPoints.Commands.AddUpdateRequestAttachments;

public class AddUpdateRequestAttachmentsCommandValidator : AbstractValidator<AddUpdateRequestAttachmentsCommand>
{
    private readonly IUploadFileService _uploadFileService;

    public AddUpdateRequestAttachmentsCommandValidator(IUploadFileService uploadFileService)
    {
        _uploadFileService = uploadFileService;

        RuleFor(x => x.UpdateRequestId).GreaterThan(0);
        RuleFor(x => x.Files)
            .NotEmpty()
            .Must(IsValidSize)
            .WithMessage(Resources.FileSizeNotValid)
            .Must(IsValidExtension)
            .WithMessage(Resources.FileExtensionNotValid);
    }

    private bool IsValidSize(IFormFileCollection files)
        => _uploadFileService.IsValidSize(files);

    private bool IsValidExtension(IFormFileCollection files)
        => _uploadFileService.IsValidExtension(files);
}
