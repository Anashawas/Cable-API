using Application.Common.Localization;
using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
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
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(20).WithName(Resources.NewPassword).WithMessage(Resources.ChangePasswordNotAllowed);
        RuleFor(x => x.CurrentPassword).NotEmpty().WithName(Resources.CurrentPassword).MustAsync(ValidateCurrentPassword).WithMessage(Resources.InvalidCurrentPassword);
    }

    private async Task<bool> ValidateCurrentPassword( ChangeMyPasswordCommand request, string currentPassword, CancellationToken cancellationToken)
    {
        var user = await _applicationDbContext.UserAccounts.FindAsync(_currentUserService.UserId.Value, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        return _passwordHasher.VerifyHashedPassword(request.CurrentPassword, user.Password) != PasswordVerificationResult.Failed;

    }

    
}