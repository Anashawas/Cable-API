using Application.Common.Localization;
using Cable.Core.Utilities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.UpdateUser;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public UpdateUserCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Name).MaximumLength(255).When(x => !string.IsNullOrEmpty(x.Name));
        RuleFor(x => x.City).NotEmpty().MaximumLength(50);

        RuleFor(x => x.Email)
            .MaximumLength(255)
            .EmailAddress()
            .MustAsync(CheckEmailIsUnique)
            .When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage(Resources.EmailMustBeUnique);
        RuleFor(x => x.RoleId).NotEmpty().MustAsync(CheckRoleExists).WithMessage(Resources.RoleMustExist);
    }

    private async Task<bool> CheckRoleExists(int id, CancellationToken cancellationToken)
        => await _applicationDbContext.Roles.AnyAsync(x => x.Id == id && !x.IsDeleted,
            cancellationToken: cancellationToken);

    private async Task<bool> CheckEmailIsUnique(UpdateUserCommand command, string? email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(email))
            return true; // Null/empty emails are allowed

        return !(await _applicationDbContext.UserAccounts.AnyAsync(
            x => x.Id != command.Id && x.Email != null && x.Email == email,
            cancellationToken: cancellationToken));
    }
}