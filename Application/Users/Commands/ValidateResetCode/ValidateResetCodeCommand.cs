using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.ValidateResetCode;

public record ValidateResetCodeCommand(
    string Email,
    string Code
) : IRequest<ValidateResetCodeDto>;

public class ValidateResetCodeCommandHandler(IApplicationDbContext context)
    : IRequestHandler<ValidateResetCodeCommand, ValidateResetCodeDto>
{
    public async Task<ValidateResetCodeDto> Handle(ValidateResetCodeCommand request, CancellationToken cancellationToken)
    {
        var user = await context.UserAccounts
            .FirstOrDefaultAsync(x => x.Email == request.Email && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User not found");
        }
        
        var passwordReset = await context.PasswordResets
            .Where(x => x.UserId == user.Id &&
                        x.Code == request.Code &&
                        !x.IsUsed &&
                        x.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (passwordReset == null)
        {
            var anyCode = await context.PasswordResets
                .Where(x => x.UserId == user.Id && x.Code == request.Code)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (anyCode is { IsUsed: true })
            {
                throw new DataValidationException("Code", "This reset code has already been used");
            }

            if (anyCode != null && anyCode.ExpiresAt <= DateTime.UtcNow)
            {
                throw new DataValidationException("Code", "This reset code has expired");
            }

            throw new DataValidationException("Code", "Invalid reset code");
        }


        if (passwordReset.FailedAttempts >= 3)
        {
            passwordReset.IsUsed = true;
            await context.SaveChanges(cancellationToken);
            throw new DataValidationException("Code", "Maximum attempts exceeded. Please request a new reset code.");
        }


        if (passwordReset.Code != request.Code)
        {
            passwordReset.FailedAttempts++;
            await context.SaveChanges(cancellationToken);

            var remainingAttempts = 3 - passwordReset.FailedAttempts;
            throw new DataValidationException("Code", $"Invalid reset code. {remainingAttempts} attempts remaining.");
        }
        
        return new ValidateResetCodeDto(
            true,
            "Reset code is valid. You can now change your password.",
            passwordReset.ExpiresAt);
    }
}
