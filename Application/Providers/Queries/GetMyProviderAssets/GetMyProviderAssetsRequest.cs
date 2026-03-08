using Application.ChargingPoints.Queries;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Queries.GetMyProviderAssets;

public record GetMyProviderAssetsRequest() : IRequest<ProviderAssetsDto>;

public record ProviderAssetsDto(
    List<GetAllChargingPointsDto> ChargingPoints,
    List<ServiceProviderDto> ServiceProviders
);

public class GetMyProviderAssetsRequestHandler(
    IChargingPointRepository chargingPointRepository,
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyProviderAssetsRequest, ProviderAssetsDto>
{
    public async Task<ProviderAssetsDto> Handle(GetMyProviderAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId!.Value;

        // Get charging points owned by current user
        var chargingPoints = await chargingPointRepository.GetChargingPointsByOwner(
            userId, null, null, cancellationToken);

        // Get service providers owned by current user
        var serviceProviders = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && x.OwnerId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var spIds = serviceProviders.Select(p => p.Id).ToList();
        var partnerProviderIds = await applicationDbContext.PartnerAgreements
            .AsNoTracking()
            .Where(pa => pa.ProviderType == "ServiceProvider" && pa.IsActive && !pa.IsDeleted
                         && spIds.Contains(pa.ProviderId))
            .Select(pa => pa.ProviderId)
            .ToListAsync(cancellationToken);
        var partnerSet = partnerProviderIds.ToHashSet();

        var serviceProviderDtos = serviceProviders.Select(x => new ServiceProviderDto(
            x.Id,
            x.Name,
            x.OwnerId,
            x.Owner?.Name,
            x.ServiceCategoryId,
            x.ServiceCategory?.Name,
            x.ServiceCategory?.NameAr,
            x.StatusId,
            x.Status?.Name,
            x.Description,
            x.Phone,
            x.Address,
            x.CountryName,
            x.CityName,
            x.Latitude,
            x.Longitude,
            x.Price,
            x.PriceDescription,
            x.FromTime,
            x.ToTime,
            x.MethodPayment,
            x.VisitorsCount,
            x.IsVerified,
            x.HasOffer,
            x.OfferDescription,
            x.Service,
            x.Icon,
            x.WhatsAppNumber,
            x.WebsiteUrl,
            x.ServiceProviderRates.Any() ? x.ServiceProviderRates.Average(r => r.Rating) : 0,
            x.ServiceProviderRates.Count,
            x.ServiceProviderAttachments.Select(a => a.FileName).ToList(),
            x.CreatedAt,
            partnerSet.Contains(x.Id)
        )).ToList();

        return new ProviderAssetsDto(chargingPoints, serviceProviderDtos);
    }
}
