using Application.Common.Interfaces;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Terms;

/// <summary>
/// Resolves which terms version applies to a caller: the active version for
/// their role wins; otherwise the active general version (RoleId = null).
/// </summary>
public static class TermsResolver
{
    public static async Task<TermsVersion?> GetActiveForRoleAsync(
        IApplicationDbContext db, int? roleId, CancellationToken ct)
    {
        TermsVersion? terms = null;
        if (roleId.HasValue)
            terms = await db.TermsVersions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.IsActive && !t.IsDeleted && t.RoleId == roleId.Value, ct);

        return terms ?? await db.TermsVersions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsActive && !t.IsDeleted && t.RoleId == null, ct);
    }

    /// <summary>Id-only variant for cheap hasAccepted checks (no MAX content columns loaded).</summary>
    public static async Task<int?> GetActiveIdForRoleAsync(
        IApplicationDbContext db, int? roleId, CancellationToken ct)
    {
        int? id = null;
        if (roleId.HasValue)
            id = await db.TermsVersions.AsNoTracking()
                .Where(t => t.IsActive && !t.IsDeleted && t.RoleId == roleId.Value)
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync(ct);

        return id ?? await db.TermsVersions.AsNoTracking()
            .Where(t => t.IsActive && !t.IsDeleted && t.RoleId == null)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);
    }
}
