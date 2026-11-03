using Cable.Core;
using Cable.Security.Encryption.Interfaces;
using Application.Workers.WorkerAuth;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Cable.Security.Encryption.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.ChangePassword;

public record ChangeMyPasswordCommand(string CurrentPassword, string NewPassword) : IRequest;

/// <summary>
/// From a partner-app token of a worker this changes the partner credential (WORKER_TRACKER §3.2.3: 8+ characters,
/// not the temporary one, not the phone; clears the forced change and signs the other partner surface out).
/// Everyone else — owners, drivers — keeps the account password as before.
/// </summary>
public class ChangeMyPasswordCommandHandler(IApplicationDbContext applicationDbContext, IPasswordHasher passwordHasher, ICurrentUserService currentUserService)
    : IRequestHandler<ChangeMyPasswordCommand>
{
    public async Task Handle(ChangeMyPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == currentUserService.UserId && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        var credential = await PartnerCredentialOf(applicationDbContext, currentUserService, user.Id, cancellationToken);
        if (credential is not null)
        {
            WorkerPasswordRules.Validate(request.NewPassword, user.Phone);
            if (passwordHasher.VerifyHashedPassword(request.NewPassword, credential.PasswordHash) != PasswordVerificationResult.Failed)
                throw new DataValidationException("NewPassword", Lang.Pick("اختر كلمة سر غير المؤقتة", "Choose a password different from the current one"));

            credential.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
            credential.MustChangePassword = false;
            credential.TempPasswordExpiresAt = null;
            credential.PasswordChangedAt = DateTime.UtcNow;
            credential.FailedLoginCount = 0;
            credential.LockedUntil = null;
            await applicationDbContext.SaveChanges(cancellationToken);

            // This device keeps its session; the other partner surface has to sign in again.
            await PartnerSessions.EndOtherAsync(applicationDbContext, user.Id, currentUserService.App, cancellationToken);
            return;
        }

        user.Password = string.IsNullOrEmpty(request.NewPassword) ? null : passwordHasher.HashPassword(request.NewPassword);
        await applicationDbContext.SaveChanges(cancellationToken);
    }

    /// <summary>The partner credential when the caller is on a partner app and is a worker; null = the account password applies.</summary>
    internal static async Task<PartnerCredential?> PartnerCredentialOf(IApplicationDbContext db, ICurrentUserService currentUser, int userId, CancellationToken ct)
    {
        if (currentUser.App is not (AuthApps.Provider or AuthApps.ProviderWeb)) return null;
        return await db.PartnerCredentials.FirstOrDefaultAsync(c => c.UserId == userId && !c.IsDeleted, ct);
    }
}
