using FluentValidation;

namespace Application.CarsManagement.CarsTypes.Commands.UpdateCarType;

public class UpdateCarTypeCommandValidator:AbstractValidator<UpdateCarTypeCommand>
{
    public UpdateCarTypeCommandValidator()
    {
        RuleFor(x=>x.Name).NotEmpty().MaximumLength(100);

    }
}