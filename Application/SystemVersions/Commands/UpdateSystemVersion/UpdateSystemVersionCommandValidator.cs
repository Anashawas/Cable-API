using FluentValidation;

namespace Application.SystemVersions.Commands.AddSystemVersionUpdate;

public class UpdateSystemVersionCommandValidator: AbstractValidator<UpdateSystemVersionCommand>
{
    public UpdateSystemVersionCommandValidator()
    {
        RuleFor(x=>x.Platform).NotEmpty();
        RuleFor(x=>x.Version).NotEmpty();
        RuleFor(x=>x.UpdateForce).NotNull();
    }
}