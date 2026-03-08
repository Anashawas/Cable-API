using FluentValidation;

namespace Application.EmergencyServiceAttachments.Commands.DeleteEmergencyServiceAttachment;

public class DeleteEmergencyServiceAttachmentCommandValidator : AbstractValidator<DeleteEmergencyServiceAttachmentCommand>
{
    public DeleteEmergencyServiceAttachmentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Emergency service ID is required")
            .GreaterThan(0)
            .WithMessage("Emergency service ID must be greater than 0");
    }
}
