using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>Guards and lookups shared by the live-charging (B) endpoints.</summary>
public static class LiveChargingAccess
{
    /// <summary>
    /// The station's owner only — not its workers, whatever their privileges (§10.5: stickers are printed by the
    /// owner). Admins pass too, for support. 404 when the station does not exist.
    /// </summary>
    public static async Task EnsureStationOwnerAsync(IApplicationDbContext db, ICurrentUserService currentUser, int chargingPointId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var station = await db.ChargingPoints.AsNoTracking()
            .Where(c => c.Id == chargingPointId && !c.IsDeleted)
            .Select(c => new { c.OwnerId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("cannot find charging point " + chargingPointId);
        if (station.OwnerId == userId) return;
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct)) return;
        throw new ForbiddenAccessException("Only the station owner can do this.");
    }

    /// <summary>The station's chargers as the apps number them: enabled, not deleted, by id → (ordinal, display name).</summary>
    public static async Task<Dictionary<int, (int Ordinal, string? DisplayName, string ChargePointId)>> ChargerOrdinalsAsync(
        IApplicationDbContext db, int chargingPointId, CancellationToken ct)
    {
        var rows = await db.OcppChargePoints.AsNoTracking()
            .Where(c => c.ChargingPointId == chargingPointId && !c.IsDeleted)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.IsEnabled, c.DisplayName, c.ChargePointId })
            .ToListAsync(ct);
        var map = new Dictionary<int, (int, string?, string)>();
        var ordinal = 0;
        foreach (var r in rows.Where(r => r.IsEnabled)) map[r.Id] = (++ordinal, Clean(r.DisplayName), r.ChargePointId);
        foreach (var r in rows.Where(r => !r.IsEnabled)) map[r.Id] = (++ordinal, Clean(r.DisplayName), r.ChargePointId);
        return map;
    }

    /// <summary>What a driver / sticker calls the cabinet: the owner's name for it, else "Charger N".</summary>
    public static string ChargerLabel(string? displayName, int ordinal, bool arabic) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : arabic ? "الشاحن " + ordinal : "Charger " + ordinal;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
