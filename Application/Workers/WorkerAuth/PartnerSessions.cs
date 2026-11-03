using Microsoft.EntityFrameworkCore;
using Cable.Core.Constants;

namespace Application.Workers.WorkerAuth;

/// <summary>Ends partner-app sessions of an account without touching the Cable (driver) app session.</summary>
public static class PartnerSessions
{
    /// <summary>Signs the account out of the partner mobile app and the partner web portal (new stamps).</summary>
    public static Task EndAllAsync(IApplicationDbContext db, int userId, CancellationToken ct)
    {
        // Computed here: EF cannot translate Guid.NewGuid().ToString("N") inside the update expression.
        var mobile = NewStamp();
        var web = NewStamp();
        return db.UserAccounts.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(u => u.ProviderSecurityStamp, mobile)
                .SetProperty(u => u.ProviderWebSecurityStamp, web), ct);
    }

    /// <summary>Signs the account out of the partner surface it is NOT using right now (the caller keeps its session).</summary>
    public static Task EndOtherAsync(IApplicationDbContext db, int userId, string currentApp, CancellationToken ct)
    {
        var stamp = NewStamp();
        return currentApp == AuthApps.ProviderWeb
            ? db.UserAccounts.Where(u => u.Id == userId).ExecuteUpdateAsync(x => x.SetProperty(u => u.ProviderSecurityStamp, stamp), ct)
            : db.UserAccounts.Where(u => u.Id == userId).ExecuteUpdateAsync(x => x.SetProperty(u => u.ProviderWebSecurityStamp, stamp), ct);
    }

    /// <summary>Forgets every trusted device: the next worker login needs an OTP again.</summary>
    public static Task ForgetDevicesAsync(IApplicationDbContext db, int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.TrustedDevices.Where(d => d.UserId == userId && !d.IsDeleted)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.IsDeleted, true).SetProperty(d => d.ModifiedAt, now), ct);
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
