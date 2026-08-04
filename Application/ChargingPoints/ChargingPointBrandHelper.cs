using Application.Common.Interfaces;
using Cable.Core;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints;

/// <summary>Input pair for setting a station's brands: which brand, how many chargers of it.</summary>
public record ChargerBrandCountInput(int ChargerBrandId, int Count);

/// <summary>
/// Shared logic for the ChargingPoint ⇄ ChargerBrand many-to-many:
/// validates the brand ids, builds the junction rows and the
/// auto-summed ChargersCount.
/// </summary>
public static class ChargingPointBrandHelper
{
    public sealed record ResolvedBrands(
        List<ChargingPointChargerBrand> Rows,
        int TotalChargers);

    public static async Task<ResolvedBrands> ResolveAsync(
        IApplicationDbContext db,
        List<ChargerBrandCountInput> brands,
        CancellationToken cancellationToken)
    {
        // Merge duplicates of the same brand; drop non-positive counts.
        var wanted = brands
            .Where(b => b.Count > 0)
            .GroupBy(b => b.ChargerBrandId)
            .Select(g => new ChargerBrandCountInput(g.Key, g.Sum(x => x.Count)))
            .ToList();

        if (wanted.Count == 0)
            throw new DataValidationException("ChargerBrands", "Provide at least one brand with a count greater than 0");

        var ids = wanted.Select(b => b.ChargerBrandId).ToList();
        var names = await db.ChargerBrands
            .Where(b => ids.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var missing = ids.Where(id => !names.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new DataValidationException("ChargerBrands",
                $"Unknown charger brand id(s): {string.Join(", ", missing)}");

        var rows = wanted.Select(b => new ChargingPointChargerBrand
        {
            ChargerBrandId = b.ChargerBrandId,
            Count = b.Count
        }).ToList();

        var total = wanted.Sum(b => b.Count);

        return new ResolvedBrands(rows, total);
    }
}
