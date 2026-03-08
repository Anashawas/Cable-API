using FluentValidation;

namespace Application.EmergencyServices.Commands.DeleteEmergencyService;

public class DeleteEmergencyServiceCommandValidator : AbstractValidator<DeleteEmergencyServiceCommand>
{
    public DeleteEmergencyServiceCommandValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Emergency service ID must be greater than 0");
    }
}
