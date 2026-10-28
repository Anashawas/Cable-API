using Application.Promos;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty;

/// <summary>
/// What a loyalty boost has actually given away — the figure its per-user / total caps and the admin "spent"
/// column count. It is derived from the transactions (awarded − base), so no counter can drift, minus the points a
/// BonusPoints promo added on the same scan: those are the promo's (paid from the promo campaign), not the boost's.
/// A winning promo multiplier clears AppliedBoostId, and a losing one or a discount promo adds no bonus of its own,
/// so only BonusPoints promos change the figure.
/// </summary>
public static class BoostSpend
{
    /// <summary>Boost extra points over these boosted, completed transactions.</summary>
    public static async Task<int> ExtraPointsAsync(IApplicationDbContext db, IQueryable<PartnerTransaction> boosted, CancellationToken ct)
    {
        var gross = await boosted.SumAsync(t => (t.PointsAwarded ?? 0) - (t.BasePoints ?? 0), ct);
        var promoBonus = await db.PromoUsages.AsNoTracking()
            .Where(u => u.TransactionType == PromoRules.PartnerScan && u.BonusPoints > 0
                        && boosted.Any(t => t.Id == u.TransactionId))
            .SumAsync(u => u.BonusPoints, ct);
        return gross - promoBonus;
    }

    /// <summary>Promo bonus points per boost id over these boosted, completed transactions (to subtract from a grouped gross).</summary>
    public static async Task<Dictionary<int, int>> PromoBonusByBoostAsync(IApplicationDbContext db, IQueryable<PartnerTransaction> boosted, CancellationToken ct) =>
        await db.PromoUsages.AsNoTracking()
            .Where(u => u.TransactionType == PromoRules.PartnerScan && u.BonusPoints > 0)
            .Join(boosted.Where(t => t.AppliedBoostId != null), u => u.TransactionId, t => t.Id, (u, t) => new { BoostId = t.AppliedBoostId!.Value, u.BonusPoints })
            .GroupBy(x => x.BoostId)
            .Select(g => new { BoostId = g.Key, Bonus = g.Sum(x => x.BonusPoints) })
            .ToDictionaryAsync(x => x.BoostId, x => x.Bonus, ct);
}
