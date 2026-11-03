using Cable.Core;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;
using Application.Workers.WorkerAuth;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.Workers.Commands.WorkerPasswordReset;

/// <summary>
/// "Forgot my password" for workers (§3.2.3): an OTP to the phone, then a new partner password. The answer is the
/// same whether or not the phone is a worker, so the endpoint does not reveal who works where.
/// </summary>
public record RequestWorkerPasswordResetCommand(string Phone) : IRequest<WorkerPasswordResetRequested>;

public record WorkerPasswordResetRequested(string Message, string PhoneMasked);

public class RequestWorkerPasswordResetCommandHandler(IApplicationDbContext db, IOtpService otp)
    : IRequestHandler<RequestWorkerPasswordResetCommand, WorkerPasswordResetRequested>
{
    public async Task<WorkerPasswordResetRequested> Handle(RequestWorkerPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var normalized = WorkerPhones.Normalize(request.Phone)
                         ?? throw new DataValidationException("Phone", Lang.Pick("رقم الموبايل غير صحيح", "Invalid phone number format."));

        var message = Lang.Pick("إذا الرقم موظف عندنا، بيوصله رمز التحقق", "If this number belongs to a worker, a verification code is on its way");
        var result = new WorkerPasswordResetRequested(message, WorkerPhones.Mask(normalized));

        var workerUserId = await WorkerOfPhoneAsync(db, normalized, cancellationToken);
        if (workerUserId is null) return result;

        if (await otp.IsRateLimitedAsync(normalized, cancellationToken))
            throw new DataValidationException("RateLimit", Lang.Pick("طلبت رموز كثيرة — جرّب بعد شوي", "Too many codes requested. Please try again later."));

        var code = await otp.GenerateOtpAsync(normalized, cancellationToken);
        if (!await otp.SendOtpAsync(normalized, code, cancellationToken))
            throw new CableApplicationException("Failed to send OTP. Please try again.");

        return result;
    }

    /// <summary>The account behind a phone when it is an active worker with a partner credential.</summary>
    internal static async Task<int?> WorkerOfPhoneAsync(IApplicationDbContext db, string normalizedPhone, CancellationToken ct) =>
        await db.UserAccounts.AsNoTracking()
            .Where(u => u.Phone == normalizedPhone && !u.IsDeleted && u.IsActive)
            .Where(u => db.PartnerCredentials.Any(c => c.UserId == u.Id && !c.IsDeleted))
            .Where(u => db.ProviderManagers.Any(pm => pm.UserId == u.Id && !pm.IsDeleted))
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(ct);
}

/// <summary>Step 2: the OTP and the new password. Ends the worker's partner sessions and forgets trusted devices.</summary>
public record ResetWorkerPasswordCommand(string Phone, string Otp, string NewPassword) : IRequest;

public class ResetWorkerPasswordCommandHandler(IApplicationDbContext db, IOtpService otp, IPasswordHasher passwordHasher)
    : IRequestHandler<ResetWorkerPasswordCommand>
{
    public async Task Handle(ResetWorkerPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalized = WorkerPhones.Normalize(request.Phone)
                         ?? throw new DataValidationException("Phone", Lang.Pick("رقم الموبايل غير صحيح", "Invalid phone number format."));
        if (string.IsNullOrWhiteSpace(request.Otp))
            throw new DataValidationException("Otp", Lang.Pick("رمز التحقق مطلوب", "The verification code is required"));

        WorkerPasswordRules.Validate(request.NewPassword, normalized);

        var workerUserId = await RequestWorkerPasswordResetCommandHandler.WorkerOfPhoneAsync(db, normalized, cancellationToken);
        // Unknown phone and wrong code get the same answer.
        if (workerUserId is null || !await otp.VerifyOtpAsync(normalized, request.Otp, cancellationToken))
            throw new NotAuthorizedAccessException(Lang.Pick("رمز التحقق غلط أو منتهي", "Invalid or expired OTP"));

        var credential = await db.PartnerCredentials.FirstAsync(c => c.UserId == workerUserId && !c.IsDeleted, cancellationToken);
        credential.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        credential.MustChangePassword = false;
        credential.TempPasswordExpiresAt = null;
        credential.PasswordChangedAt = DateTime.UtcNow;
        credential.FailedLoginCount = 0;
        credential.LockedUntil = null;
        await db.SaveChanges(cancellationToken);

        await PartnerSessions.ForgetDevicesAsync(db, workerUserId.Value, cancellationToken);
        await PartnerSessions.EndAllAsync(db, workerUserId.Value, cancellationToken);
    }
}
