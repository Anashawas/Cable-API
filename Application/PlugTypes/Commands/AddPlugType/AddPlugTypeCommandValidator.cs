using FluentValidation;

namespace Application.PlugTypes.Commands.AddPlugType;

public class AddPlugTypeCommandValidator : AbstractValidator<AddPlugTypeCommand>
{
    public AddPlugTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x=>x.SerialNumber).NotEmpty().MaximumLength(255);
    }
}