using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.UpdateUser;

public class UpdateUserCommandValidator: AbstractValidator<UpdateUserCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public UpdateUserCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(255).MustAsync(CheckUserNameIsUnique)
            .WithMessage(Resources.UserNameMustBeUnique);
        RuleFor(x => x.Email).MaximumLength(255).EmailAddress().MustAsync(CheckEmailIsUnique).When(x => !string.IsNullOrEmpty(x.Email)).WithMessage(Resources.EmailMustBeUnique);
        RuleFor(x => x.RoleId).NotEmpty().MustAsync(CheckRoleExists).WithMessage(Resources.RoleMustExist);
    }

    private async Task<bool> CheckRoleExists(int id, CancellationToken cancellationToken)
        => await _applicationDbContext.Roles.AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: cancellationToken);

    private async Task<bool> CheckEmailIsUnique(UpdateUserCommand command, string email, CancellationToken cancellationToken)
        => string.IsNullOrEmpty(email) ? true : !(await _applicationDbContext.UserAccounts.AnyAsync(x => x.Id != command.Id && x.Email == email, cancellationToken: cancellationToken));

    private async Task<bool> CheckUserNameIsUnique(UpdateUserCommand command, string userName, CancellationToken cancellationToken)
        => !(await _applicationDbContext.UserAccounts.AnyAsync(x => x.Id != command.Id && x.UserName == userName, cancellationToken: cancellationToken));

}