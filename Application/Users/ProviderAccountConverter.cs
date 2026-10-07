using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Users;

/// <summary>
/// The single rule that keeps Provider/Worker accounts sign-in-able.
///
/// The partner apps accept email + password only — <c>LoginProvider</c> rejects
/// any account still carrying a Google/Apple identity (RegistrationProvider or
/// FirebaseUId). Granting a provider-family role therefore has to strip that
/// identity, and it can only do so once a password exists — clearing it from a
/// passwordless account would lock the user out of BOTH apps.
///
/// Role and password are set through SEPARATE endpoints (UpdateUser vs
/// ChangePassword), and the account can reach the finished state through either
/// order. So both call this, on whichever completes the pair last:
///  - promote first, password later  -> ChangePassword clears it
///  - password first, promote later  -> UpdateUser clears it
/// A real Apple-signup provider was refused at partner login for months because
/// only the first path ran the conversion (see AkroshStation, 2026-09).
/// </summary>
public static class ProviderAccountConverter
{
    /// <summary>
    /// Clears the social identity when <paramref name="user"/> is (being made) a
    /// Provider/Worker that still carries one. No-op for every other account, so
    /// it is safe to call unconditionally after a role or password change. Throws
    /// when the account is provider-family and social but has no password yet —
    /// that combination cannot sign in anywhere, so it must be refused, not saved.
    /// </summary>
    public static async Task EnsureConvertedAsync(
        IApplicationDbContext db, UserAccount user, int roleId, CancellationToken cancellationToken)
    {
        var isProviderFamily = await db.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Id == roleId
                           && !r.IsDeleted
                           && (r.Name == "Provider" || r.Name == "Worker"),
                cancellationToken);

        if (!isProviderFamily)
            return;

        var hasSocialIdentity = !string.IsNullOrEmpty(user.RegistrationProvider)
                                || !string.IsNullOrEmpty(user.FirebaseUId);

        if (!hasSocialIdentity)
            return;

        if (string.IsNullOrEmpty(user.Password))
        {
            throw new DataValidationException("Password",
                "This account signs in with Google or Apple. Set a password for it first, " +
                "then grant Provider or Worker access — the partner apps do not accept social sign-in.");
        }

        user.RegistrationProvider = null;
        user.FirebaseUId = null;
    }
}
