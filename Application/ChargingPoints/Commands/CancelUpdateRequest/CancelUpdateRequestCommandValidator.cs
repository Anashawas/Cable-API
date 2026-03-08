using FluentValidation;

namespace Application.ChargingPoints.Commands.CancelUpdateRequest;

public class CancelUpdateRequestCommandValidator : AbstractValidator<CancelUpdateRequestCommand>
{
    public CancelUpdateRequestCommandValidator()
    {
        RuleFor(x => x.UpdateRequestId)
            .GreaterThan(0)
            .WithMessage("Update request ID must be greater than 0");
    }
}
