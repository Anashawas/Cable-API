using FluentValidation;

namespace Application.CarsManagement.CarsTypes.Commands.AddCarTypeCommand;

public class AddCarTypeCommandValidator : AbstractValidator<AddCarTypeCommand>
{
    public AddCarTypeCommandValidator()
    {
        RuleFor(x=>x.Name).NotEmpty().MaximumLength(100);
    }
}