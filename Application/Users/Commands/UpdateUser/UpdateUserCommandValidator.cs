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

        RuleFor(x => x.Phone)
            .NotEmpty()
            .Must(PhoneNumberUtility.IsValidJordanPhoneNumber)
            .WithMessage($"Phone number must be a valid Jordan mobile number. Supported formats: {string.Join(", ", PhoneNumberUtility.GetSupportedFormats())}")
            .MustAsync(CheckPhoneIsUnique)
            .WithMessage("Phone number must be unique");

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

    private async Task<bool> CheckPhoneIsUnique(UpdateUserCommand command, string phone,
        CancellationToken cancellationToken)
    {
        var normalizedPhone = PhoneNumberUtility.NormalizePhoneNumber(phone);
        if (normalizedPhone == null)
            return false; // Invalid phone format
            
        return !(await _applicationDbContext.UserAccounts.AnyAsync(
            x => x.Id != command.Id && x.Phone == normalizedPhone && !x.IsDeleted,
            cancellationToken: cancellationToken));
    }
}