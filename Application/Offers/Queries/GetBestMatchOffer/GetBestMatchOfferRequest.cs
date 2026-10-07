using Application.Common.Utilities;
using Application.Offers.Queries.Common;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetBestMatchOffer;

public record BestMatchOfferDto(
    ProviderOfferSummaryDto Offer,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    string? CityName,
    /// <summary>True when the caller's points already cover this offer.</summary>
    bool IsAffordable,
    /// <summary>Points minus cost: 0 or more is spare change, negative is the shortfall.</summary>
    int PointsDifference
);

public record GetBestMatchOfferResult(
    string RequestedCity,
    /// <summary>The canonical city the request resolved to, or null if unrecognised.</summary>
    string? ResolvedCity,
    int Points,
    BestMatchOfferDto? BestMatch,
    IReadOnlyList<BestMatchOfferDto> Alternatives
);

/// <summary>
/// "Given this city and this many points, what should I show the user?"
///
/// Best match is the most expensive offer the points already cover — closest to
/// the balance from below, so the user gets the most for what they have rather
/// than the cheapest thing available. If nothing is affordable yet, the nearest
/// offer ABOVE the balance is returned with <c>IsAffordable=false</c> and the
/// shortfall in <c>PointsDifference</c>, which gives the app something
/// motivating to show instead of an empty screen.
/// </summary>
public record GetBestMatchOfferRequest(string City, int Points, int? AlternativesLimit = null)
    : IRequest<GetBestMatchOfferResult>;

public class GetBestMatchOfferRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetBestMatchOfferRequest, GetBestMatchOfferResult>
{
    private const int DefaultAlternativesLimit = 5;

    public async Task<GetBestMatchOfferResult> Handle(GetBestMatchOfferRequest request,
        CancellationToken cancellationToken)
    {
        var resolvedCity = JordanCityResolver.Resolve(request.City);

        // An unrecognised city is an empty result, not an error: the app asked a
        // reasonable question and the answer is "nothing here".
        if (resolvedCity is null)
            return new GetBestMatchOfferResult(request.City, null, request.Points, null, []);

        var chargingPoints = await applicationDbContext.ChargingPoints.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CityName == resolvedCity)
            .Select(x => new { x.Id, x.Name, x.CityName })
            .ToListAsync(cancellationToken);

        var serviceProviders = await applicationDbContext.ServiceProviders.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CityName == resolvedCity)
            .Select(x => new { x.Id, x.Name, x.CityName })
            .ToListAsync(cancellationToken);

        if (chargingPoints.Count == 0 && serviceProviders.Count == 0)
            return new GetBestMatchOfferResult(request.City, resolvedCity, request.Points, null, []);

        var chargingPointIds = chargingPoints.Select(p => p.Id).ToList();
        var serviceProviderIds = serviceProviders.Select(p => p.Id).ToList();

        var offersByChargingPoint = await ProviderOfferSummaryLoader.LoadByProviderAsync(
            applicationDbContext, uploadFileService, "ChargingPoint", chargingPointIds, cancellationToken);

        var offersByServiceProvider = await ProviderOfferSummaryLoader.LoadByProviderAsync(
            applicationDbContext, uploadFileService, "ServiceProvider", serviceProviderIds, cancellationToken);

        var candidates = new List<BestMatchOfferDto>();

        foreach (var provider in chargingPoints)
        {
            foreach (var offer in offersByChargingPoint.GetValueOrDefault(provider.Id, []))
                candidates.Add(ToCandidate(offer, "ChargingPoint", provider.Id, provider.Name, provider.CityName, request.Points));
        }

        foreach (var provider in serviceProviders)
        {
            foreach (var offer in offersByServiceProvider.GetValueOrDefault(provider.Id, []))
                candidates.Add(ToCandidate(offer, "ServiceProvider", provider.Id, provider.Name, provider.CityName, request.Points));
        }

        if (candidates.Count == 0)
            return new GetBestMatchOfferResult(request.City, resolvedCity, request.Points, null, []);

        var affordable = candidates.Where(c => c.IsAffordable)
            // Closest to the balance from below. Ties break toward the offer
            // worth more cash, so equal-cost offers are not ordered arbitrarily.
            .OrderByDescending(c => c.Offer.PointsCost)
            .ThenByDescending(c => c.Offer.MonetaryValue)
            .ToList();

        var ranked = affordable.Count > 0
            ? affordable
            : candidates
                .OrderBy(c => c.Offer.PointsCost)
                .ThenByDescending(c => c.Offer.MonetaryValue)
                .ToList();

        var limit = request.AlternativesLimit is > 0 ? request.AlternativesLimit.Value : DefaultAlternativesLimit;

        return new GetBestMatchOfferResult(
            request.City,
            resolvedCity,
            request.Points,
            ranked[0],
            ranked.Skip(1).Take(limit).ToList());
    }

    private static BestMatchOfferDto ToCandidate(ProviderOfferSummaryDto offer, string providerType,
        int providerId, string? providerName, string? cityName, int points)
        => new(offer, providerType, providerId, providerName, cityName,
            offer.PointsCost <= points, points - offer.PointsCost);
}
