using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.Common;

/// <summary>
/// The compact view of an offer used when offers are embedded inside another
/// resource (a service provider in a list, a best-match result). Deliberately
/// smaller than <c>OfferDto</c>: approval fields, proposer identity and usage
/// counters are provider/admin concerns and have no place in a consumer list.
/// </summary>
public record ProviderOfferSummaryDto(
    int Id,
    string Title,
    string? TitleAr,
    string? Description,
    string? DescriptionAr,
    int PointsCost,
    decimal? PointsPriceValue,
    decimal MonetaryValue,
    string CurrencyCode,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo
);

public static class ProviderOfferSummaryLoader
{
    /// <summary>
    /// Offers that a consumer can actually redeem right now: approved, active,
    /// inside the validity window, and not already at their global use cap.
    /// </summary>
    public static IQueryable<Domain.Enitites.ProviderOffer> RedeemableNow(
        IApplicationDbContext applicationDbContext, DateTime now)
        => applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.IsActive
                        && x.ApprovalStatus == (int)OfferApprovalStatus.Approved
                        && x.ValidFrom <= now
                        && (x.ValidTo == null || x.ValidTo >= now)
                        && (x.MaxTotalUses == null || x.CurrentTotalUses < x.MaxTotalUses));

    /// <summary>
    /// Loads the redeemable offers for many providers in ONE query, keyed by
    /// provider id. Called once per list request — querying per provider would
    /// turn a 50-item page into 51 round trips.
    /// </summary>
    public static async Task<Dictionary<int, List<ProviderOfferSummaryDto>>> LoadByProviderAsync(
        IApplicationDbContext applicationDbContext,
        IUploadFileService uploadFileService,
        string providerType,
        IReadOnlyCollection<int> providerIds,
        CancellationToken cancellationToken)
    {
        if (providerIds.Count == 0)
            return new Dictionary<int, List<ProviderOfferSummaryDto>>();

        var rows = await RedeemableNow(applicationDbContext, DateTime.UtcNow)
            .Where(x => x.ProviderType == providerType && providerIds.Contains(x.ProviderId))
            .OrderBy(x => x.PointsCost)
            .Select(x => new
            {
                x.ProviderId,
                x.Id,
                x.Title,
                x.TitleAr,
                x.Description,
                x.DescriptionAr,
                x.PointsCost,
                x.PointsPriceValue,
                x.MonetaryValue,
                x.CurrencyCode,
                x.ImageUrl,
                x.ValidFrom,
                x.ValidTo
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ProviderId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new ProviderOfferSummaryDto(
                    r.Id, r.Title, r.TitleAr, r.Description, r.DescriptionAr,
                    r.PointsCost, r.PointsPriceValue, r.MonetaryValue, r.CurrencyCode,
                    string.IsNullOrEmpty(r.ImageUrl)
                        ? null
                        : uploadFileService.GetFilePath(UploadFileFolders.CableOfferAttachments, r.ImageUrl),
                    r.ValidFrom, r.ValidTo)).ToList());
    }
}
