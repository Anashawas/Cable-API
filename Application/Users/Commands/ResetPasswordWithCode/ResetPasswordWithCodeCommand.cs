using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.ResetPasswordWithCode;

public record ResetPasswordWithCodeCommand(
    string Email,
    string Code,
    string NewPassword
) : IRequest<ResetPasswordWithCodeDto>;

public class ResetPasswordWithCodeCommandHandler : IRequestHandler<ResetPasswordWithCodeCommand, ResetPasswordWithCodeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public ResetPasswordWithCodeCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<ResetPasswordWithCodeDto> Handle(ResetPasswordWithCodeCommand request, CancellationToken cancellationToken)
    {
        // Find user by email
        var user = await _context.UserAccounts
            .FirstOrDefaultAsync(x => x.Email == request.Email && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        // Find the password reset record (code should already be validated by ValidateResetCodeCommand)
        var passwordReset = await _context.PasswordResets
            .Where(x => x.UserId == user.Id &&
                        x.Code == request.Code &&
                        !x.IsUsed &&
                        x.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (passwordReset == null)
        {
            throw new DataValidationException("Reset-Code", "Invalid or expired reset code");
        }

        // Reset the password
        user.Password = _passwordHasher.HashPassword(request.NewPassword);

        // Mark the reset code as used
        passwordReset.IsUsed = true;
        passwordReset.UsedAt = DateTime.UtcNow;

        await _context.SaveChanges(cancellationToken);

        return new ResetPasswordWithCodeDto(
            true,
            "Password reset successfully");
    }
}
