using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.AddUser;

public class AddUserCommandValidator : AbstractValidator<AddUserCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;
    
    public AddUserCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Username).NotEmpty().MaximumLength(255).MustAsync(CheckUserNameIsUnique)
            .WithMessage(Resources.UserNameMustBeUnique);
    
        RuleFor(x => x.RoleId).NotEmpty().MustAsync(CheckRoleExists).WithMessage(Resources.RoleMustExist);
    }
    
    private async Task<bool> CheckUserNameIsUnique(string userName, CancellationToken cancellationToken)
        => !(await _applicationDbContext.UserAccounts.AnyAsync(x => x.UserName == userName, cancellationToken));
    
    private async Task<bool> CheckRoleExists(int id, CancellationToken cancellationToken)
        => await _applicationDbContext.Roles.AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
}