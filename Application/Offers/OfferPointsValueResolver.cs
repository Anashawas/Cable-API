using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers;

/// <summary>
/// B1 — resolves an offer's PointsPriceValue (the cash worth of its points).
/// When the client sends a value it is stored as sent (the admin may round);
/// when omitted it is derived from the active conversion rate for the offer's
/// currency: pointsCost ÷ pointsPerUnit.
/// </summary>
public static class OfferPointsValueResolver
{
    public static async Task<decimal?> ResolveAsync(
        IApplicationDbContext db,
        decimal? provided,
        int pointsCost,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        if (provided.HasValue)
            return provided;

        var rate = await db.PointsConversionRates.AsNoTracking()
            .Where(r => r.IsActive && !r.IsDeleted && r.CurrencyCode == currencyCode)
            .OrderByDescending(r => r.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);

        if (rate is not { PointsPerUnit: > 0 })
            return null;

        return Math.Round(pointsCost / (decimal)rate.PointsPerUnit, 3);
    }
}
