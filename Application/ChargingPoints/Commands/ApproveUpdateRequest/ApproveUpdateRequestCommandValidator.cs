using Application.Common.Interfaces;
using FluentValidation;

namespace Application.ChargingPoints.Commands.ApproveUpdateRequest;

public class ApproveUpdateRequestCommandValidator : AbstractValidator<ApproveUpdateRequestCommand>
{
    public ApproveUpdateRequestCommandValidator()
    {
        RuleFor(x => x.UpdateRequestId)
            .GreaterThan(0)
            .WithMessage("Update request ID must be greater than 0");
    }
}
