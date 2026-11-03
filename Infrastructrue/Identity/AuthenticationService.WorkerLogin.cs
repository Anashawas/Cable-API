using Cable.Core;
using Application.Common.Models;
using Application.Workers.WorkerAuth;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Cable.Security.Encryption.Models;
using Domain.Enitites;
using Infrastructrue.Common.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Infrastructrue.Identity;

/// <summary>
/// Worker login by phone + partner password (WORKER_TRACKER §3.2.3). The partner credential is a second password
/// on the one account; the driver password is never consulted. Unknown phone, wrong password and "not a worker"
/// all answer 401 INVALID_CREDENTIALS so the endpoint does not reveal who is a worker.
/// </summary>
public partial class AuthenticationService
{
    public const string InvalidCredentialsCode = "INVALID_CREDENTIALS";
    public const string LockedCode = "LOCKED";
    public const string TempPasswordExpiredCode = "TEMP_PASSWORD_EXPIRED";
    public const string WorkerInactiveCode = "WORKER_INACTIVE";

    public async Task<WorkerLoginResult> LoginWorkerByPhoneAsync(string phone, string password, string deviceId, string? deviceToken,
        string app, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone)) throw new DataValidationException("Phone", Lang.Pick("رقم الموبايل مطلوب", "The phone number is required"));
        if (string.IsNullOrWhiteSpace(password)) throw new DataValidationException("Password", Lang.Pick("كلمة السر مطلوبة", "The password is required"));
        if (string.IsNullOrWhiteSpace(deviceId)) throw new DataValidationException("DeviceId", Lang.Pick("معرّف الجهاز مطلوب", "deviceId is required"));

        var normalized = WorkerPhones.Normalize(phone) ?? throw InvalidCredentials();
        var settings = await WorkerAuthSettings.ReadAsync(_applicationDbContext, cancellationToken);
        var now = DateTime.UtcNow;

        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(u => u.Phone == normalized && !u.IsDeleted, cancellationToken);
        var credential = user is null
            ? null
            : await _applicationDbContext.PartnerCredentials.FirstOrDefaultAsync(c => c.UserId == user.Id && !c.IsDeleted, cancellationToken);
        if (user is null || credential is null) throw InvalidCredentials();

        if (credential.LockedUntil is DateTime lockedUntil && lockedUntil > now) throw Locked(lockedUntil);

        if (VerifyPassword(password, credential.PasswordHash) == PasswordVerificationResult.Failed)
        {
            credential.FailedLoginCount++;
            if (credential.FailedLoginCount >= settings.MaxFailedLogins)
            {
                credential.LockedUntil = now.AddMinutes(settings.LockMinutes);
                credential.FailedLoginCount = 0;
                await _applicationDbContext.SaveChanges(cancellationToken);
                throw Locked(credential.LockedUntil.Value);
            }
            await _applicationDbContext.SaveChanges(cancellationToken);
            throw InvalidCredentials();
        }

        // The password matched: now the account state, the assignment and the temp-password expiry
        // (after the password check, so an outsider cannot probe which phones are workers).
        if (!user.IsActive) throw new NotAuthorizedAccessException(Resources.DeactivatedUser);
        var assignment = await _applicationDbContext.ProviderManagers.AsNoTracking()
            .Where(pm => pm.UserId == user.Id && !pm.IsDeleted)
            .Select(pm => new { pm.IsActive, pm.SuspendedByPlanAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (assignment is null) throw InvalidCredentials();
        if (!assignment.IsActive || assignment.SuspendedByPlanAt is not null)
            throw new CodedHttpException(403, WorkerInactiveCode, assignment.IsActive
                ? Lang.Pick("باقة المحطة ما بتسمح بدخولك حالياً — تواصل مع صاحب المحطة", "The station's plan does not allow your login right now — contact the station owner")
                : Lang.Pick("أوقف صاحب المحطة حسابك", "The station owner has suspended your account"));

        if (credential.TempPasswordExpiresAt is DateTime expiresAt && expiresAt < now)
            throw new CodedHttpException(410, TempPasswordExpiredCode,
                Lang.Pick("انتهت صلاحية كلمة السر المؤقتة — اطلب من صاحب المحطة يشاركها مرة ثانية",
                          "The temporary password has expired — ask the station owner to share it again"));

        credential.FailedLoginCount = 0;
        credential.LockedUntil = null;

        // Same device as an earlier OTP login → straight to the tokens.
        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            var hash = TrustedDeviceTokens.Hash(deviceToken);
            var device = await _applicationDbContext.TrustedDevices
                .FirstOrDefaultAsync(d => d.UserId == user.Id && d.TokenHash == hash && d.App == app && !d.IsDeleted, cancellationToken);
            if (device is not null && device.ExpiresAt > now)
            {
                device.LastUsedAt = now;
                device.DeviceId = deviceId;
                device.ExpiresAt = now.AddDays(settings.TrustedDeviceDays);   // sliding
                credential.LastLoginAt = now;
                await _applicationDbContext.SaveChanges(cancellationToken);

                var login = await GetUserLoginDetails(user, CheckUserDetailsCompleted(user), cancellationToken, app: app);
                return new WorkerLoginResult(false, null, null, null, null,
                    login with { MustChangePassword = credential.MustChangePassword, DeviceToken = deviceToken });
            }
        }
        await _applicationDbContext.SaveChanges(cancellationToken);

        // First login or a new device: the OTP step, through the same session as the owner login.
        if (await _otpService.IsRateLimitedAsync(user.Phone!, cancellationToken))
            throw new DataValidationException("RateLimit", Lang.Pick("طلبت رموز كثيرة — جرّب بعد شوي", "Too many codes requested. Please try again later."));

        var sessionToken = Guid.NewGuid().ToString("N");
        var session = new ProviderAuthSession
        {
            UserId = user.Id, PhoneNumber = user.Phone!, CreatedAt = now, ExpiresAt = now.AddMinutes(10),
            IsWorkerLogin = true, DeviceId = deviceId.Trim()
        };
        await _cache.SetAsync($"provider-auth:{sessionToken}", session,
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(10) },
            cancellationToken: cancellationToken);

        var otp = await _otpService.GenerateOtpAsync(user.Phone!, cancellationToken);
        if (!await _otpService.SendOtpAsync(user.Phone!, otp, cancellationToken))
            throw new CableApplicationException("Failed to send OTP. Please try again.");

        return new WorkerLoginResult(true, sessionToken, WorkerPhones.Mask(user.Phone), session.ExpiresAt, "sms", null);
    }

    /// <summary>
    /// After a worker's OTP: trust the device, record the first login (owner push), and tell the app whether the
    /// temporary password still has to be changed.
    /// </summary>
    private async Task<UserLoginResult> CompleteWorkerOtpLoginAsync(UserAccount user, ProviderAuthSession session, UserLoginResult login,
        string app, CancellationToken cancellationToken)
    {
        var settings = await WorkerAuthSettings.ReadAsync(_applicationDbContext, cancellationToken);
        var now = DateTime.UtcNow;

        var credential = await _applicationDbContext.PartnerCredentials.FirstOrDefaultAsync(c => c.UserId == user.Id && !c.IsDeleted, cancellationToken);
        if (credential is null) return login;

        var firstLogin = credential.FirstLoginAt is null;
        credential.FirstLoginAt ??= now;
        credential.LastLoginAt = now;

        // One trusted token per (device, app): an older one for the same device is replaced.
        var deviceId = session.DeviceId ?? "";
        await _applicationDbContext.TrustedDevices
            .Where(d => d.UserId == user.Id && d.App == app && d.DeviceId == deviceId && !d.IsDeleted)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.IsDeleted, true).SetProperty(d => d.ModifiedAt, now), cancellationToken);

        var token = TrustedDeviceTokens.New();
        _applicationDbContext.TrustedDevices.Add(new TrustedDevice
        {
            UserId = user.Id, App = app, DeviceId = deviceId, TokenHash = TrustedDeviceTokens.Hash(token),
            LastUsedAt = now, ExpiresAt = now.AddDays(settings.TrustedDeviceDays)
        });
        await _applicationDbContext.SaveChanges(cancellationToken);

        if (firstLogin) await _workerLoginNotifier.NotifyFirstLoginAsync(user.Id, cancellationToken);

        return login with { MustChangePassword = credential.MustChangePassword, DeviceToken = token };
    }

    private static CodedHttpException InvalidCredentials() =>
        new(401, InvalidCredentialsCode, Lang.Pick("رقم الموبايل أو كلمة السر غلط", "Wrong phone number or password"));

    private static CodedHttpException Locked(DateTime untilUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((untilUtc - DateTime.UtcNow).TotalMinutes));
        return new CodedHttpException(423, LockedCode,
            Lang.Pick($"الدخول مقفول مؤقتاً — جرّب بعد {minutes} دقيقة", $"Login is temporarily locked — try again in {minutes} minute(s)"),
            new Dictionary<string, object?> { ["until"] = JordanTime.FromUtc(untilUtc), ["retryAfterMinutes"] = minutes });
    }
}
