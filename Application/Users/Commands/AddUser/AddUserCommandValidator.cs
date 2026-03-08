using Application.Common.Localization;
using Cable.Core.Utilities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.AddUser;

public class AddUserCommandValidator : AbstractValidator<AddUserCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public AddUserCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Name).MaximumLength(255).When(x => !string.IsNullOrEmpty(x.Name));

        RuleFor(x => x.RoleId).NotEmpty().MustAsync(CheckRoleExists).WithMessage(Resources.RoleMustExist);
        RuleFor(x => x.Email)
            .MustAsync(CheckEmailUnique)
            .When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage(Resources.EmailMustBeUnique);
    }


    private async Task<bool> CheckRoleExists(int id, CancellationToken cancellationToken)
        => await _applicationDbContext.Roles.AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);

    private async Task<bool> CheckEmailUnique(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(email))
            return true; // Null/empty emails are allowed

        return !await _applicationDbContext.UserAccounts.AnyAsync(
            x => x.Email != null && x.Email.ToLower() == email.ToLower(),
            cancellationToken);
    }
}