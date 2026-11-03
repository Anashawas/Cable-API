using Cable.Core;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.Workers.WorkerAuth;

/// <summary>What the owner gets back whenever a temporary password is issued (create, "share again", admin support).</summary>
public record WorkerTempPasswordResult(
    int ProviderManagerId,
    int WorkerUserId,
    /// <summary>Shown once. The server keeps only the hash.</summary>
    string TempPassword,
    DateTime TempPasswordExpiresAt,
    /// <summary>The ready-to-send text of the share screen, in the request language.</summary>
    string ShareMessage);

/// <summary>Shared steps of issuing a temporary partner password.</summary>
public static class WorkerCredentials
{
    /// <summary>
    /// Creates or resets the worker's partner credential to a temporary password: forced change on the first
    /// login, expiry per settings, lock and failed count cleared. The caller saves.
    /// </summary>
    public static async Task<(string TempPassword, DateTime ExpiresAt)> IssueTempPasswordAsync(
        IApplicationDbContext db, IPasswordHasher hasher, WorkerAuthSettings settings, int userId, string? requestedTempPassword,
        CancellationToken ct)
    {
        var temp = string.IsNullOrWhiteSpace(requestedTempPassword) ? TempPasswords.Generate() : requestedTempPassword.Trim();
        if (temp.Length < TempPasswords.Length || temp.Length > WorkerPasswordRules.MaxLength)
            throw new DataValidationException("TempPassword",
                Lang.Pick($"كلمة السر المؤقتة لازم تكون {TempPasswords.Length} خانات على الأقل", $"The temporary password must be at least {TempPasswords.Length} characters"));

        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(settings.TempPasswordHours);

        var credential = await db.PartnerCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (credential is null)
        {
            credential = new PartnerCredential { UserId = userId };
            db.PartnerCredentials.Add(credential);
        }
        credential.IsDeleted = false;
        credential.PasswordHash = hasher.HashPassword(temp);
        credential.MustChangePassword = true;
        credential.TempPasswordExpiresAt = expiresAt;
        credential.FailedLoginCount = 0;
        credential.LockedUntil = null;
        return (temp, expiresAt);
    }

    /// <summary>The names the share message needs: the owner (or the admin acting for them) and the provider.</summary>
    public static async Task<(string OwnerName, string ProviderName)> NamesAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var provider = providerType == WorkerOwnershipHelper.ChargingPoint
            ? await db.ChargingPoints.AsNoTracking().Where(c => c.Id == providerId).Select(c => new { c.Name, c.OwnerId }).FirstOrDefaultAsync(ct)
            : await db.ServiceProviders.AsNoTracking().Where(s => s.Id == providerId).Select(s => new { s.Name, s.OwnerId }).FirstOrDefaultAsync(ct);
        if (provider is null) throw new NotFoundException($"{providerType} with id {providerId} not found");

        var ownerName = provider.OwnerId is int ownerId
            ? await db.UserAccounts.AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.Name).FirstOrDefaultAsync(ct)
            : null;
        return (string.IsNullOrWhiteSpace(ownerName) ? Lang.Pick("صاحب المحطة", "The owner") : ownerName, provider.Name);
    }
}
