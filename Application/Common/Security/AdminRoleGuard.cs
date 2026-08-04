using Application.Common.Interfaces;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Security;

/// <summary>
/// Shared guard for admin-portal endpoints: the caller must be an active user
/// with the Admin role. Interim role-based gating until the privilege system
/// (ApplicationAuthorize / Privilage tables) is seeded and activated.
/// </summary>
public static class AdminRoleGuard
{
    /// <summary>Role table: 2 = Admin, 3 = User, 4 = Provider, 5 = Worker.</summary>
    public const int AdminRoleId = 2;

    public static async Task EnsureAdminAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            throw new NotAuthorizedAccessException("User not authenticated");

        if (!await IsAdminAsync(db, currentUser, cancellationToken))
            throw new ForbiddenAccessException("Admin role required.");
    }

    /// <summary>Non-throwing variant for owner-OR-admin checks.</summary>
    public static async Task<bool> IsAdminAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return false;

        var roleId = await db.UserAccounts
            .AsNoTracking()
            .Where(x => x.Id == currentUser.UserId.Value && !x.IsDeleted && x.IsActive)
            .Select(x => (int?)x.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        return roleId == AdminRoleId;
    }
}
