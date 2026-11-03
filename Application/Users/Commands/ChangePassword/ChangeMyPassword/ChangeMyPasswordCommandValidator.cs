using Cable.Core.Exceptions;
using Cable.Core;
using Cable.Security.Encryption.Interfaces;
using Application.Common.Localization;
using Cable.Security.Encryption.Models;
using FluentValidation;

namespace Application.Users.Commands.ChangePassword;

public class ChangeMyPasswordCommandValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPasswordHasher _passwordHasher;

    public ChangeMyPasswordCommandValidator(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService, IPasswordHasher passwordHasher)
    {
        _applicationDbContext = applicationDbContext;
        _currentUserService = currentUserService;
        _passwordHasher = passwordHasher;
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(64).WithName(Resources.NewPassword).WithMessage(Resources.ChangePasswordNotAllowed);
        RuleFor(x => x.CurrentPassword).NotEmpty().WithName(Resources.CurrentPassword).MustAsync(ValidateCurrentPassword).WithMessage(Resources.InvalidCurrentPassword);
    }

    private async Task<bool> ValidateCurrentPassword( ChangeMyPasswordCommand request, string currentPassword, CancellationToken cancellationToken)
    {
        var user = await _applicationDbContext.UserAccounts.FindAsync(_currentUserService.UserId.Value, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        // A worker on a partner app proves the partner password, not the (driver) account password.
        var credential = await ChangeMyPasswordCommandHandler.PartnerCredentialOf(_applicationDbContext, _currentUserService, user.Id, cancellationToken);
        var hash = credential?.PasswordHash ?? user.Password;
        if (string.IsNullOrEmpty(hash)) return false;

        return _passwordHasher.VerifyHashedPassword(request.CurrentPassword, hash) != PasswordVerificationResult.Failed;
    }
}
