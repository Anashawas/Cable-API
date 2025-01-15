using FluentValidation;

namespace Application.PlugTypes.Commands.UpdateplugType;

public class UpdatePlugTypeCommandValidator : AbstractValidator<UpdatePlugTypeCommand>
{
    public UpdatePlugTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.SerialNumber).NotEmpty().MaximumLength(255);
    }
}