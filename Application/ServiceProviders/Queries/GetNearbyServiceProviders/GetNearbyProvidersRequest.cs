using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.GetNearbyServiceProviders;

public record GetNearbyProvidersRequest(double Latitude, double Longitude, double RadiusKm = 10)
    : IRequest<List<ServiceProviderDto>>;

public class GetNearbyProvidersRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetNearbyProvidersRequest, List<ServiceProviderDto>>
{
    public async Task<List<ServiceProviderDto>> Handle(GetNearbyProvidersRequest request,
        CancellationToken cancellationToken)
    {
        // Approximate bounding box filter (1 degree ≈ 111 km)
        var latDelta = request.RadiusKm / 111.0;
        var lngDelta = request.RadiusKm / (111.0 * Math.Cos(request.Latitude * Math.PI / 180.0));

        var providers = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted
                         && x.Latitude >= request.Latitude - latDelta
                         && x.Latitude <= request.Latitude + latDelta
                         && x.Longitude >= request.Longitude - lngDelta
                         && x.Longitude <= request.Longitude + lngDelta)
            .ToListAsync(cancellationToken);

        var providerIds = providers.Select(p => p.Id).ToList();
        var partnerProviderIds = await applicationDbContext.PartnerAgreements
            .AsNoTracking()
            .Where(pa => pa.ProviderType == "ServiceProvider" && pa.IsActive && !pa.IsDeleted
                         && providerIds.Contains(pa.ProviderId))
            .Select(pa => pa.ProviderId)
            .ToListAsync(cancellationToken);
        var partnerSet = partnerProviderIds.ToHashSet();

        return providers.Select(x => new ServiceProviderDto(
            x.Id, x.Name, x.OwnerId, x.Owner?.Name,
            x.ServiceCategoryId, x.ServiceCategory?.Name, x.ServiceCategory?.NameAr,
            x.StatusId, x.Status?.Name, x.Description, x.Phone,
            x.Address, x.CountryName, x.CityName, x.Latitude, x.Longitude,
            x.Price, x.PriceDescription, x.FromTime, x.ToTime, x.MethodPayment,
            x.VisitorsCount, x.IsVerified, x.HasOffer, x.OfferDescription,
            x.Service, x.Icon, x.WhatsAppNumber, x.WebsiteUrl,
            x.ServiceProviderRates.Any() ? x.ServiceProviderRates.Average(r => r.Rating) : 0,
            x.ServiceProviderRates.Count,
            x.ServiceProviderAttachments.Select(a => a.FileName).ToList(),
            x.CreatedAt,
            partnerSet.Contains(x.Id)
        )).ToList();
    }
}
