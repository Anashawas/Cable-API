using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Queries.GetUserRating;

/// <summary>
/// Shared read path for a driver's rating, so the driver's own view, the
/// provider-facing lookup and the scan preview cannot drift apart in how the
/// average is calculated.
/// </summary>
public static class UserRatingReader
{
    /// <summary>Average and count only — for callers that show a score, not a list.</summary>
    public static async Task<(double? Average, int Count)> GetAverageAsync(
        IApplicationDbContext applicationDbContext, int userId, CancellationToken cancellationToken)
    {
        var scores = await applicationDbContext.UserRates.AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .Select(x => x.Rating)
            .ToListAsync(cancellationToken);

        if (scores.Count == 0)
            return (null, 0);

        // Rounded to one decimal: the raw double renders as 4.333333333333333
        // in JSON, which every client would then have to trim.
        return (Math.Round(scores.Average(), 1), scores.Count);
    }

    public static async Task<UserRatingSummaryDto> GetSummaryAsync(
        IApplicationDbContext applicationDbContext, int userId, CancellationToken cancellationToken)
    {
        var rows = await applicationDbContext.UserRates.AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Rating,
                x.Comment,
                x.ProviderType,
                x.ProviderId,
                x.PartnerTransactionId,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new UserRatingSummaryDto(userId, null, 0, []);

        // Provider names come from two tables, so they are resolved in two
        // batched lookups rather than per row.
        var chargingPointIds = rows.Where(r => r.ProviderType == "ChargingPoint").Select(r => r.ProviderId).Distinct().ToList();
        var serviceProviderIds = rows.Where(r => r.ProviderType == "ServiceProvider").Select(r => r.ProviderId).Distinct().ToList();

        var chargingPointNames = chargingPointIds.Count == 0
            ? []
            : await applicationDbContext.ChargingPoints.AsNoTracking()
                .Where(x => chargingPointIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var serviceProviderNames = serviceProviderIds.Count == 0
            ? []
            : await applicationDbContext.ServiceProviders.AsNoTracking()
                .Where(x => serviceProviderIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var entries = rows.Select(r => new UserRatingEntryDto(
            r.Id,
            r.Rating,
            r.Comment,
            r.ProviderType,
            r.ProviderId,
            r.ProviderType == "ChargingPoint"
                ? chargingPointNames.GetValueOrDefault(r.ProviderId)
                : serviceProviderNames.GetValueOrDefault(r.ProviderId),
            r.PartnerTransactionId,
            r.CreatedAt)).ToList();

        return new UserRatingSummaryDto(
            userId,
            Math.Round(rows.Average(r => r.Rating), 1),
            rows.Count,
            entries);
    }
}
