using FluentValidation;

namespace Application.Users.Commands.ChangePassword;

public class ChangePasswordCommandValidator: AbstractValidator<ChangePasswordCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;
    
    public ChangePasswordCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;

        RuleFor(x => x.Password).NotEmpty().MinimumLength(3).MaximumLength(20);
    }
    
}