using FluentValidation;

namespace Application.ChargingPoints.Commands.RejectUpdateRequest;

public class RejectUpdateRequestCommandValidator : AbstractValidator<RejectUpdateRequestCommand>
{
    public RejectUpdateRequestCommandValidator()
    {
        RuleFor(x => x.UpdateRequestId)
            .GreaterThan(0)
            .WithMessage("Update request ID must be greater than 0");

        RuleFor(x => x.RejectionReason)
            .NotEmpty()
            .WithMessage("Rejection reason is required")
            .MaximumLength(1000)
            .WithMessage("Rejection reason cannot exceed 1000 characters");
    }
}
