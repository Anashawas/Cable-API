using FluentValidation;

namespace Application.SystemVersions.Commands.AddSystemVersion;

public class AddSystemVersionCommandValidator : AbstractValidator<AddSystemVersionCommand>
{
    public AddSystemVersionCommandValidator()
    {
        RuleFor(x => x.Platform).NotEmpty();
        RuleFor(x => x.Version).NotEmpty();
        RuleFor(x => x.ForceUpdate).NotEmpty();
    }
}