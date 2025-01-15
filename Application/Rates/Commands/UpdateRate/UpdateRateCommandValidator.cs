using FluentValidation;

namespace Application.Rates.Commands.UpdateRate;

public class UpdateRateCommandValidator: AbstractValidator<UpdateRateCommand>
{
    public UpdateRateCommandValidator()
    {
        RuleFor(x=>x.Id).NotEmpty();
        RuleFor(x => x.ChargingPointRate).NotEmpty();
    }
}